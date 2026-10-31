#pragma once
#include "pch.h"
#include "EncodedMediaSink.h"

namespace W10MWebCam
{
    namespace Native
    {
        /// Raised for every encoded H.264 sample (usually one video frame). sampleTime is in 100 ns units.
        /// queueDelay: wait in the native hand-off queue; deviceLatency: sensor capture -> encoder output. 100 ns units (-1 = unknown).
        public delegate void FrameHandler(const Platform::Array<uint8>^ data, int64 sampleTime, bool keyframe, int64 queueDelay, int64 deviceLatency);
        public delegate void NativeLogHandler(Platform::String^ message);

        /// The object the C# app uses: pass MediaExtension to MediaCapture.StartRecordToCustomSinkAsync.
        public ref class EncodedFrameSink sealed
        {
        public:
            EncodedFrameSink(Windows::Media::MediaProperties::IMediaEncodingProperties^ videoProperties);
            virtual ~EncodedFrameSink();

            property Windows::Media::IMediaExtension^ MediaExtension
            {
                Windows::Media::IMediaExtension^ get();
            }

            /// SPS/PPS from the encoder's output media type (may be empty).
            Platform::Array<uint8>^ GetSequenceHeader();

            event FrameHandler^ FrameReceived;
            event NativeLogHandler^ LogMessage;

        internal:
            void RaiseFrame(const BYTE* data, DWORD length, LONGLONG sampleTime, bool keyframe, LONGLONG queueDelay, LONGLONG deviceLatency);
            void RaiseLog(const wchar_t* message);
            void ReportCallbackError(const wchar_t* what, int hr);

        private:
            Microsoft::WRL::ComPtr<EncodedMediaSink> _sink;
            bool _hooked;
            int _callbackErrors = 0;
        };
    }
}
