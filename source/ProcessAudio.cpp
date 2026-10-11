// Application-only WASAPI loopback. No input devices or microphone APIs are used.
// Architecture based on Microsoft's documented ApplicationLoopback API.
#include <windows.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl.h>
#include <wrl/implements.h>
#include <fstream>
#include <iostream>
#include <iomanip>
#include <vector>
#include <algorithm>
#include <stdexcept>
#include <cstdint>
#include <cmath>
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;
using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::FtmBase;

static void check(HRESULT hr) {
    if (FAILED(hr)) { char text[80]; sprintf_s(text, "Windows audio error 0x%08X", (unsigned)hr); throw std::runtime_error(text); }
}
struct Handle {
    HANDLE value;
    explicit Handle(HANDLE h): value(h) { if (!h) throw std::runtime_error("Cannot create/open synchronization event"); }
    ~Handle() { CloseHandle(value); }
    operator HANDLE() const { return value; }
};
class Activation final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IActivateAudioInterfaceCompletionHandler, FtmBase> {
public:
    Handle done{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    HRESULT result = E_PENDING;
    ComPtr<IAudioClient> client;
    STDMETHOD(ActivateCompleted)(IActivateAudioInterfaceAsyncOperation* op) override {
        ComPtr<IUnknown> object;
        HRESULT activationResult = E_FAIL;
        result = op->GetActivateResult(&activationResult, &object);
        if (SUCCEEDED(result)) result = activationResult;
        if (SUCCEEDED(result)) result = object.As(&client);
        SetEvent(done);
        return S_OK;
    }
};
static uint64_t qpc100ns() {
    LARGE_INTEGER n, f; QueryPerformanceCounter(&n); QueryPerformanceFrequency(&f);
    return (uint64_t)((long double)n.QuadPart * 10000000.L / f.QuadPart);
}
static double unixNow() {
    FILETIME ft; GetSystemTimePreciseAsFileTime(&ft);
    ULARGE_INTEGER n; n.LowPart=ft.dwLowDateTime; n.HighPart=ft.dwHighDateTime;
    return (n.QuadPart - 116444736000000000ULL) / 10000000.;
}
// Standard PCM WAV; recording is deliberately limited to stay below RIFF's 4 GB boundary.
class Wave {
    std::fstream file;
public:
    uint64_t frames=0;
    explicit Wave(const wchar_t* path): file(path, std::ios::binary|std::ios::in|std::ios::out|std::ios::trunc) {
        if (!file) throw std::runtime_error("Cannot create game audio file");
        header();
    }
    void word(uint16_t v) { file.write((char*)&v,2); }
    void dword(uint32_t v) { file.write((char*)&v,4); }
    void header() {
        file.seekp(0); file.write("RIFF",4); dword((uint32_t)(36+frames*4)); file.write("WAVEfmt ",8);
        dword(16); word(1); word(2); dword(48000); dword(192000); word(4); word(16);
        file.write("data",4); dword((uint32_t)(frames*4)); file.seekp(0,std::ios::end);
    }
    void append(const BYTE* data, uint64_t count) {
        if ((frames+count)*4 > 0xFFF00000ULL) throw std::runtime_error("Audio file size limit reached (about six hours)");
        static std::vector<char> zeros(48000*4,0);
        while (count) {
            auto n=(size_t)std::min<uint64_t>(count,48000);
            file.write(data ? (const char*)data : zeros.data(), n*4);
            if (!file) throw std::runtime_error("Could not write game audio (check free disk space)");
            if (data) data+=n*4;
            frames+=n; count-=n;
        }
    }
    ~Wave() { if (file) { header(); file.flush(); } }
};

int wmain(int argc, wchar_t** argv) {
    if(argc==2 && wcscmp(argv[1],L"--displays")==0) {
        ComPtr<ID3D11Device> device;
        HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,nullptr);
        if(FAILED(hr)) return 1;
        ComPtr<IDXGIDevice> dxgi; device.As(&dxgi);
        ComPtr<IDXGIAdapter> adapter; dxgi->GetAdapter(&adapter);
        for(UINT i=0;;++i) {
            ComPtr<IDXGIOutput> output;
            if(adapter->EnumOutputs(i,&output)==DXGI_ERROR_NOT_FOUND) break;
            DXGI_OUTPUT_DESC desc{}; output->GetDesc(&desc);
            std::wcout<<i<<L"\t"<<desc.DeviceName<<std::endl;
        }
        return 0;
    }
    if (argc!=5) { std::cerr<<"Usage: ProcessAudio <pid> <wave path> <stop event> <owner pid>\n"; return 2; }
    HRESULT co=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    try {
        check(co);
        DWORD pid=wcstoul(argv[1],nullptr,10);
        if (!pid) throw std::runtime_error("Choose a valid game process");
        Handle stop(OpenEventW(SYNCHRONIZE,FALSE,argv[3]));
        Handle owner(OpenProcess(SYNCHRONIZE,FALSE,wcstoul(argv[4],nullptr,10)));
        auto callback=Microsoft::WRL::Make<Activation>();
        AUDIOCLIENT_ACTIVATION_PARAMS params{};
        params.ActivationType=AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
        params.ProcessLoopbackParams.TargetProcessId=pid;
        params.ProcessLoopbackParams.ProcessLoopbackMode=PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
        PROPVARIANT prop{}; prop.vt=VT_BLOB; prop.blob.cbSize=sizeof(params); prop.blob.pBlobData=(BYTE*)&params;
        ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
        check(ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,__uuidof(IAudioClient),&prop,callback.Get(),&operation));
        if (WaitForSingleObject(callback->done,10000)!=WAIT_OBJECT_0) throw std::runtime_error("Windows did not activate game audio within ten seconds");
        check(callback->result);
        auto client=callback->client;
        WAVEFORMATEX fmt{}; fmt.wFormatTag=WAVE_FORMAT_PCM; fmt.nChannels=2; fmt.nSamplesPerSec=48000;
        fmt.wBitsPerSample=16; fmt.nBlockAlign=4; fmt.nAvgBytesPerSec=192000;
        check(client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_LOOPBACK|AUDCLNT_STREAMFLAGS_EVENTCALLBACK|AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,0,0,&fmt,nullptr));
        Handle samples(CreateEventW(nullptr,FALSE,FALSE,nullptr));
        check(client->SetEventHandle(samples));
        ComPtr<IAudioCaptureClient> capture;
        check(client->GetService(__uuidof(IAudioCaptureClient),(void**)capture.GetAddressOf()));
        Wave wave(argv[2]);
        uint64_t base=qpc100ns(); double unixStart=unixNow();
        check(client->Start());
        std::cout<<"READY "<<std::fixed<<std::setprecision(7)<<unixStart<<std::endl;
        HANDLE events[]={stop.value,owner.value,samples.value};
        bool stopping=false;
        uint64_t headerAt=0;
        while (!stopping) {
            DWORD wait=WaitForMultipleObjects(3,events,FALSE,100);
            if (wait==WAIT_FAILED) throw std::runtime_error("Audio wait failed");
            stopping=(wait==WAIT_OBJECT_0 || wait==WAIT_OBJECT_0+1);
            UINT32 available=0; check(capture->GetNextPacketSize(&available));
            while (available) {
                BYTE* data=nullptr; UINT32 frames=0; DWORD flags=0; UINT64 device=0,stamp=0;
                check(capture->GetBuffer(&data,&frames,&flags,&device,&stamp));
                // QPC packet positions retain pauses and silence, avoiding accumulated drift.
                int64_t start = (flags&AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR) ? (int64_t)wave.frames :
                    (int64_t)llround(((int64_t)stamp-(int64_t)base)*48000.L/10000000.L);
                if (start>(int64_t)wave.frames) wave.append(nullptr,(uint64_t)start-wave.frames);
                uint64_t skip=start<(int64_t)wave.frames ? (uint64_t)((int64_t)wave.frames-start) : 0;
                if (skip<frames) wave.append((flags&AUDCLNT_BUFFERFLAGS_SILENT)?nullptr:data+skip*4,frames-skip);
                check(capture->ReleaseBuffer(frames));
                check(capture->GetNextPacketSize(&available));
            }
            // Some applications produce no packets while silent. Keep the file on wall time,
            // leaving a 250ms grace period for delayed packets except at shutdown.
            auto elapsed=(qpc100ns()-base)*48000ULL/10000000ULL;
            auto deadline=stopping?elapsed:(elapsed>12000?elapsed-12000:0);
            if (deadline>wave.frames) wave.append(nullptr,deadline-wave.frames);
            if(wave.frames-headerAt>=48000) {wave.header();headerAt=wave.frames;}
        }
        check(client->Stop());
        std::cout<<"DONE "<<wave.frames<<std::endl;
        CoUninitialize(); return 0;
    } catch (const std::exception& e) {
        std::cerr<<e.what()<<std::endl;
        if (SUCCEEDED(co)) CoUninitialize();
        return 1;
    }
}
