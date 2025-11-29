#include <windows.h>
#include <unknwn.h>
#include <objbase.h>
#include "JitProfilerPlugin.h"

class __declspec(uuid("{DF9EDC4B-25C1-4925-A3FB-6AAEB3E2FACD}")) ProfilerCLSID;

static volatile long g_componentCount = 0;
static volatile long g_lockCount = 0;

//=============================================================================
// ClassFactory for managing instances of the profiler
//=============================================================================

class JitProfilerClassFactory : public IClassFactory
{
public:
    JitProfilerClassFactory()
        : refCount(1)
    {
        InterlockedIncrement(&g_componentCount);
    }

    virtual ~JitProfilerClassFactory()
    {
        InterlockedDecrement(&g_componentCount);
    }

    // IUnknown methods
    ULONG STDMETHODCALLTYPE AddRef()
    {
        return InterlockedIncrement(&refCount);
    }

    ULONG STDMETHODCALLTYPE Release()
    {
        auto ret = InterlockedDecrement(&refCount);
        if (ret == 0)
        {
            delete(this);
        }
        return ret;
    }

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppInterface)
    {
        if (ppInterface == NULL)
            return E_INVALIDARG;

        if (riid == IID_IUnknown)
        {
            *ppInterface = static_cast<IUnknown*>(this);
        }
        else if (riid == IID_IClassFactory)
        {
            *ppInterface = static_cast<IClassFactory*>(this);
        }
        else
        {
            *ppInterface = NULL;
            return E_NOINTERFACE;
        }

        reinterpret_cast<IUnknown*>(*ppInterface)->AddRef();
        return S_OK;
    }

    // IClassFactory methods
    HRESULT STDMETHODCALLTYPE CreateInstance(
        IUnknown* pUnkOuter,
        REFIID riid,
        void** ppInterface)
    {
        if (ppInterface == NULL)
            return E_INVALIDARG;

        if (NULL != pUnkOuter)
            return CLASS_E_NOAGGREGATION;

        JitProfilerPlugin* pProfilerCallback = new JitProfilerPlugin();
        if (pProfilerCallback == NULL)
            return E_OUTOFMEMORY;

        HRESULT hr = pProfilerCallback->QueryInterface(riid, ppInterface);
        if (FAILED(hr))
        {
            pProfilerCallback->Release();
        }

        return hr;
    }

    HRESULT STDMETHODCALLTYPE LockServer(BOOL bLock)
    {
        if (bLock)
        {
            InterlockedIncrement(&g_lockCount);
        }
        else
        {
            InterlockedDecrement(&g_lockCount);
        }

        return S_OK;
    }

private:
    long refCount;
};

//=============================================================================
// DLL Entry Points
//=============================================================================

BOOL WINAPI DllMain(HINSTANCE hInstance, DWORD dwReason, LPVOID lpReserved)
{
    switch (dwReason)
    {
    case DLL_PROCESS_ATTACH:
        ProfilerLogger::Initialize();
        JitProfilerPlugin::InitializeMaxRecurseDepth();
        break;

    case DLL_PROCESS_DETACH:
        ProfilerLogger::CloseLogFiles();
        break;

    case DLL_THREAD_ATTACH:
        break;

    case DLL_THREAD_DETACH:
        break;
    }

    return TRUE;
}

//=============================================================================
// DLL Export Functions
//=============================================================================

STDAPI DllCanUnloadNow(void)
{
    if (g_componentCount == 0 && g_lockCount == 0)
        return S_OK;
    return S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv)
{
    if (rclsid != __uuidof(ProfilerCLSID))
        return CLASS_E_CLASSNOTAVAILABLE;

    JitProfilerClassFactory* pFactory = new JitProfilerClassFactory();
    if (NULL == pFactory)
        return E_OUTOFMEMORY;

    HRESULT hr = pFactory->QueryInterface(riid, ppv);
    pFactory->Release();
    return hr;
}

STDAPI DllRegisterServer(void)
{
    return S_OK;
}

STDAPI DllUnregisterServer(void)
{
    return S_OK;
}
