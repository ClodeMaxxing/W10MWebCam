#include "pch.h"
#include "EncodedFrameSink.h"

using namespace W10MWebCam::Native;
using namespace Microsoft::WRL;
using namespace Platform;

EncodedFrameSink::EncodedFrameSink(Windows::Media::MediaProperties::IMediaEncodingProperties^ videoProperties)
    : _hooked(false)
{
    MFStartup(MF_VERSION, MFSTARTUP_LITE);

    ComPtr<IMFMediaType> type;
    if (videoProperties != nullptr)
    {
        HRESULT hr = MFCreateMediaTypeFromProperties(reinterpret_cast<IUnknown*>(videoProperties), &type);
        if (FAILED(hr)) type = nullptr;
    }

    HRESULT hr = MakeAndInitialize<EncodedMediaSink>(&_sink, type.Get());
    if (FAILED(hr)) throw Exception::CreateException(hr, L"Could not create the native media sink");
}

EncodedFrameSink::~EncodedFrameSink()
{
    if (_sink) _sink->ClearCallbacks();
    _sink = nullptr;
    MFShutdown();
}

Windows::Media::IMediaExtension^ EncodedFrameSink::MediaExtension::get()
{
    if (!_hooked)
    {
        _hooked = true;
        WeakReference weak(this);
        _sink->SetCallbacks(
            [weak](const BYTE* data, DWORD length, LONGLONG time, bool keyframe, LONGLONG queueDelay, LONGLONG deviceLatency)
            {
                auto self = weak.Resolve<EncodedFrameSink>();
                if (self != nullptr) self->RaiseFrame(data, length, time, keyframe, queueDelay, deviceLatency);
            },
            [weak](const wchar_t* message)
            {
                auto self = weak.Resolve<EncodedFrameSink>();
                if (self != nullptr) self->RaiseLog(message);
            });
    }
    // Hand out our own reference: reinterpret_cast to a ^ does not AddRef, but the ^ releases when done.
    ComPtr<ABI::Windows::Media::IMediaExtension> extension;
    _sink.As(&extension);
    return reinterpret_cast<Windows::Media::IMediaExtension^>(extension.Detach());
}

Array<uint8>^ EncodedFrameSink::GetSequenceHeader()
{
    auto header = _sink->GetSequenceHeader();
    auto result = ref new Array<uint8>((unsigned int)header.size());
    if (!header.empty()) memcpy(result->Data, header.data(), header.size());
    return result;
}

void EncodedFrameSink::RaiseFrame(const BYTE* data, DWORD length, LONGLONG sampleTime, bool keyframe, LONGLONG queueDelay, LONGLONG deviceLatency)
{
    // Runs on the sink's worker thread. An exception escaping from here would reach std::terminate and kill the app
    // (crash dump: 0xC0000409 fast-fail in ucrtbase), so catch everything and report it as a log line instead.
    try
    {
        auto copy = ref new Array<uint8>(const_cast<uint8*>(data), length);
        FrameReceived(copy, sampleTime, keyframe, queueDelay, deviceLatency);
    }
    catch (Platform::Exception^ e) { ReportCallbackError(L"video frame", e->HResult); }
    catch (...) { ReportCallbackError(L"video frame", 0); }
}

void EncodedFrameSink::RaiseLog(const wchar_t* message)
{
    try { LogMessage(ref new String(message)); }
    catch (...) { }
}

void EncodedFrameSink::ReportCallbackError(const wchar_t* what, int hr)
{
    // Limit to a handful of lines so a handler that always throws cannot flood the log.
    if (_callbackErrors++ >= 5) return;
    wchar_t buffer[160];
    swprintf_s(buffer, L"The app's %s handler threw an exception (0x%08X) - ignored", what, (unsigned)hr);
    RaiseLog(buffer);
}
