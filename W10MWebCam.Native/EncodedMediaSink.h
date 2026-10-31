#pragma once
#include "pch.h"

// A Media Foundation "media sink" that MediaCapture.StartRecordToCustomSinkAsync can record into.
// Instead of writing a file, it hands every encoded H.264 sample straight to a callback,
// with no MP4 muxer in between (the muxer was what held video back for ~350 ms).

namespace W10MWebCam
{
    namespace Native
    {
        // queueDelay: how long the frame waited in the hand-off queue before reaching the app, 100 ns units.
        // deviceLatency: camera sensor capture -> encoder output, from the sample's capture timestamp (-1 = unknown).
        typedef std::function<void(const BYTE* data, DWORD length, LONGLONG sampleTime, bool keyframe, LONGLONG queueDelay, LONGLONG deviceLatency)> FrameCallback;
        typedef std::function<void(const wchar_t* message)> LogCallback;

        class EncodedStreamSink;

        class EncodedMediaSink : public Microsoft::WRL::RuntimeClass<
            Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::RuntimeClassType::WinRtClassicComMix>,
            ABI::Windows::Media::IMediaExtension,
            IMFMediaSink,
            IMFClockStateSink,
            Microsoft::WRL::FtmBase>
        {
            InspectableClass(L"W10MWebCam.Native.EncodedMediaSink", BaseTrust)

        public:
            EncodedMediaSink();
            virtual ~EncodedMediaSink();
            HRESULT RuntimeClassInitialize(IMFMediaType* videoType);

            void SetCallbacks(FrameCallback onFrame, LogCallback onLog);
            void ClearCallbacks();
            void Log(const wchar_t* format, ...);
            /// Called on the Media Foundation thread: copies the frame and returns immediately.
            void DeliverFrame(const BYTE* data, DWORD length, LONGLONG sampleTime, bool keyframe, LONGLONG captureLatency, LONGLONG deviceLatency);
            LONGLONG ClockStartSystemTime() const { return _clockStartSystemTime; }
            std::vector<BYTE> GetSequenceHeader();

            // IMediaExtension
            IFACEMETHOD(SetProperties)(ABI::Windows::Foundation::Collections::IPropertySet* configuration);

            // IMFMediaSink
            IFACEMETHOD(GetCharacteristics)(DWORD* characteristics);
            IFACEMETHOD(AddStreamSink)(DWORD streamSinkIdentifier, IMFMediaType* mediaType, IMFStreamSink** streamSink);
            IFACEMETHOD(RemoveStreamSink)(DWORD streamSinkIdentifier);
            IFACEMETHOD(GetStreamSinkCount)(DWORD* streamSinkCount);
            IFACEMETHOD(GetStreamSinkByIndex)(DWORD index, IMFStreamSink** streamSink);
            IFACEMETHOD(GetStreamSinkById)(DWORD streamSinkIdentifier, IMFStreamSink** streamSink);
            IFACEMETHOD(SetPresentationClock)(IMFPresentationClock* presentationClock);
            IFACEMETHOD(GetPresentationClock)(IMFPresentationClock** presentationClock);
            IFACEMETHOD(Shutdown)();

            // IMFClockStateSink
            IFACEMETHOD(OnClockStart)(MFTIME systemTime, LONGLONG clockStartOffset);
            IFACEMETHOD(OnClockStop)(MFTIME systemTime);
            IFACEMETHOD(OnClockPause)(MFTIME systemTime);
            IFACEMETHOD(OnClockRestart)(MFTIME systemTime);
            IFACEMETHOD(OnClockSetRate)(MFTIME systemTime, float rate);

        private:
            Microsoft::WRL::ComPtr<EncodedStreamSink> FindStream(DWORD id);

            std::recursive_mutex _lock;
            std::mutex _callbackLock;
            bool _shutdown;
            std::vector<Microsoft::WRL::ComPtr<EncodedStreamSink>> _streams;
            Microsoft::WRL::ComPtr<IMFPresentationClock> _clock;
            FrameCallback _onFrame;
            LogCallback _onLog;

            // Frames are handed to the app on this worker thread, so the camera pipeline never waits on app code.
            struct PendingFrame { std::vector<BYTE> data; LONGLONG time; bool keyframe; LONGLONG enqueuedAt; LONGLONG deviceLatency; };
            bool _waitForKeyframe;
            volatile LONGLONG _clockStartSystemTime;
            void WorkerLoop();
            void StopWorker();
            std::thread _worker;
            std::mutex _queueLock;
            std::condition_variable _queueSignal;
            std::deque<PendingFrame> _queue;
            bool _stopWorker;
            unsigned _dropped;
        };

        class EncodedStreamSink : public Microsoft::WRL::RuntimeClass<
            Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
            Microsoft::WRL::ChainInterfaces<IMFStreamSink, IMFMediaEventGenerator>,
            IMFMediaTypeHandler>
        {
        public:
            EncodedStreamSink();
            virtual ~EncodedStreamSink();
            HRESULT RuntimeClassInitialize(EncodedMediaSink* sink, DWORD id, IMFMediaType* mediaType);

            DWORD Id() const { return _id; }
            HRESULT Start();
            HRESULT Stop();
            HRESULT Pause();
            HRESULT Restart();
            void Shutdown();
            std::vector<BYTE> SequenceHeader();

            // IMFMediaEventGenerator
            IFACEMETHOD(GetEvent)(DWORD flags, IMFMediaEvent** event);
            IFACEMETHOD(BeginGetEvent)(IMFAsyncCallback* callback, IUnknown* state);
            IFACEMETHOD(EndGetEvent)(IMFAsyncResult* result, IMFMediaEvent** event);
            IFACEMETHOD(QueueEvent)(MediaEventType type, REFGUID extendedType, HRESULT status, const PROPVARIANT* value);

            // IMFStreamSink
            IFACEMETHOD(GetMediaSink)(IMFMediaSink** mediaSink);
            IFACEMETHOD(GetIdentifier)(DWORD* identifier);
            IFACEMETHOD(GetMediaTypeHandler)(IMFMediaTypeHandler** handler);
            IFACEMETHOD(ProcessSample)(IMFSample* sample);
            IFACEMETHOD(PlaceMarker)(MFSTREAMSINK_MARKER_TYPE markerType, const PROPVARIANT* markerValue, const PROPVARIANT* contextValue);
            IFACEMETHOD(Flush)();

            // IMFMediaTypeHandler
            IFACEMETHOD(IsMediaTypeSupported)(IMFMediaType* mediaType, IMFMediaType** closestMediaType);
            IFACEMETHOD(GetMediaTypeCount)(DWORD* typeCount);
            IFACEMETHOD(GetMediaTypeByIndex)(DWORD index, IMFMediaType** mediaType);
            IFACEMETHOD(SetCurrentMediaType)(IMFMediaType* mediaType);
            IFACEMETHOD(GetCurrentMediaType)(IMFMediaType** mediaType);
            IFACEMETHOD(GetMajorType)(GUID* majorType);

        private:
            Microsoft::WRL::ComPtr<IMFMediaEventQueue> GetQueue();

            std::recursive_mutex _lock;
            bool _shutdown;
            DWORD _id;
            EncodedMediaSink* _sink; // owner; valid until Shutdown
            Microsoft::WRL::ComPtr<IMFMediaEventQueue> _queue;
            Microsoft::WRL::ComPtr<IMFMediaType> _type;
            LONGLONG _samples;
        };
    }
}
