#pragma once

#include <windows.h>
#include <cor.h>
#include <corprof.h>
#include <atlbase.h>
#include <atlcom.h>
#include <vector>
#include <unordered_set>
#include <string>
#include <cstdio>
#include <cstdarg>
#include <cstring>

void __stdcall GlobalEnter3Callback(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo);

struct TypeArgInfo {
    ModuleID moduleId;
    mdTypeDef typeDef;
    std::vector<TypeArgInfo> nestedTypeArgs;
};

class ProfilerLogger
{
public:
    static FILE* g_jitLogFile;
    static FILE* g_enter3LogFile;
    static FILE* g_moduleLogFile;

    static bool OpenLogFiles();
    static void CloseLogFiles();

    static void LogJIT(const wchar_t* format, ...)
    {
        if (!format) return;
        EnterCriticalSection(&g_jitLogLock);
        if (g_jitLogFile != nullptr)
        {
            va_list args;
            va_start(args, format);
            vfwprintf(g_jitLogFile, format, args);
            fwprintf(g_jitLogFile, L"\n");
            fflush(g_jitLogFile);
            va_end(args);
        }
        LeaveCriticalSection(&g_jitLogLock);
    }

    static void LogEnter3(const wchar_t* format, ...)
    {
        if (!format) return;
        EnterCriticalSection(&g_enter3LogLock);
        if (g_enter3LogFile != nullptr)
        {
            va_list args;
            va_start(args, format);
            vfwprintf(g_enter3LogFile, format, args);
            fwprintf(g_enter3LogFile, L"\n");
            fflush(g_enter3LogFile);
            va_end(args);
        }
        LeaveCriticalSection(&g_enter3LogLock);
    }

    static void LogModule(const wchar_t* format, ...)
    {
        if (!format) return;
        EnterCriticalSection(&g_moduleLogLock);
        if (g_moduleLogFile != nullptr)
        {
            va_list args;
            va_start(args, format);
            vfwprintf(g_moduleLogFile, format, args);
            fwprintf(g_moduleLogFile, L"\n");
            fflush(g_moduleLogFile);
            va_end(args);
        }
        LeaveCriticalSection(&g_moduleLogLock);
    }

    static void Initialize()
    {
        if (!g_initialized)
        {
            InitializeCriticalSection(&g_jitLogLock);
            InitializeCriticalSection(&g_enter3LogLock);
            InitializeCriticalSection(&g_moduleLogLock);
            OpenLogFiles();
            g_initialized = true;
        }
    }

private:
    static void GetLogPath(const wchar_t* filename, wchar_t* outPath, size_t maxLen)
    {
        std::wstring basePath = L"C:\\siglocal";
        DWORD envLen = GetEnvironmentVariableW(L"SIG_JIT_PROFILER_LOG_PATH", nullptr, 0);
        if (envLen > 0) {
            std::wstring envPath(envLen, L'\0');
            GetEnvironmentVariableW(L"SIG_JIT_PROFILER_LOG_PATH", &envPath[0], envLen);
            basePath = envPath.substr(0, wcsnlen_s(envPath.c_str(), envPath.size()));
        }

        std::wstring fullPath = basePath + L"\\" + filename;
        wcsncpy_s(outPath, maxLen, fullPath.c_str(), _TRUNCATE);
    }

    static CRITICAL_SECTION g_jitLogLock;
    static CRITICAL_SECTION g_enter3LogLock;
    static CRITICAL_SECTION g_moduleLogLock;
    static bool g_initialized;
};

class JitProfilerPlugin : public ICorProfilerCallback4
{
public:
    JitProfilerPlugin();
    ~JitProfilerPlugin();

    // IUnknown
    STDMETHOD_(ULONG, AddRef)()
    {
        return InterlockedIncrement(&refCount);
    }

    STDMETHOD_(ULONG, Release)()
    {
        auto ret = InterlockedDecrement(&refCount);
        if (ret == 0)
            delete(this);
        return ret;
    }

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppvObject) override
    {
        if (riid == __uuidof(ICorProfilerCallback4) ||
            riid == __uuidof(ICorProfilerCallback3) ||
            riid == __uuidof(ICorProfilerCallback2) ||
            riid == __uuidof(ICorProfilerCallback) ||
            riid == IID_IUnknown)
        {
            *ppvObject = this;
            this->AddRef();
            return S_OK;
        }

        *ppvObject = nullptr;
        return E_NOINTERFACE;
    }

    // ICorProfilerCallback (core methods)
    STDMETHOD(Initialize)(IUnknown* pICorProfilerInfoUnk);
    STDMETHOD(Shutdown)();

    // App domain events - not logged
    STDMETHOD(AppDomainCreationStarted)(AppDomainID appDomainId) { return S_OK; }
    STDMETHOD(AppDomainCreationFinished)(AppDomainID appDomainId, HRESULT hrStatus) { return S_OK; }
    STDMETHOD(AppDomainShutdownStarted)(AppDomainID appDomainId) { return S_OK; }
    STDMETHOD(AppDomainShutdownFinished)(AppDomainID appDomainId, HRESULT hrStatus) { return S_OK; }

    // Assembly events - not logged
    STDMETHOD(AssemblyLoadStarted)(AssemblyID assemblyId) { return S_OK; }
    STDMETHOD(AssemblyLoadFinished)(AssemblyID assemblyId, HRESULT hrStatus) { return S_OK; }
    STDMETHOD(AssemblyUnloadStarted)(AssemblyID assemblyId) { return S_OK; }
    STDMETHOD(AssemblyUnloadFinished)(AssemblyID assemblyId, HRESULT hrStatus) { return S_OK; }

    // Module events - not logged
    STDMETHOD(ModuleLoadStarted)(ModuleID moduleId) { return S_OK; }
    STDMETHOD(ModuleLoadFinished)(ModuleID moduleId, HRESULT hrStatus) { return S_OK; }
    STDMETHOD(ModuleUnloadStarted)(ModuleID moduleId) { return S_OK; }
    STDMETHOD(ModuleUnloadFinished)(ModuleID moduleId, HRESULT hrStatus) { return S_OK; }
    STDMETHOD(ModuleAttachedToAssembly)(ModuleID moduleId, AssemblyID assemblyId) { return S_OK; }

    // Class events - not logged
    STDMETHOD(ClassLoadStarted)(ClassID classId) { return S_OK; }
    STDMETHOD(ClassLoadFinished)(ClassID classId, HRESULT hrStatus) { return S_OK; }
    STDMETHOD(ClassUnloadStarted)(ClassID classId) { return S_OK; }
    STDMETHOD(ClassUnloadFinished)(ClassID classId, HRESULT hrStatus) { return S_OK; }

    // Function events
    STDMETHOD(FunctionUnloadStarted)(FunctionID functionId) { return S_OK; }
    STDMETHOD(JITCompilationStarted)(FunctionID functionId, BOOL fIsSafeToBlock);
    STDMETHOD(JITCompilationFinished)(FunctionID functionId, HRESULT hrStatus, BOOL fIsSafeToBlock) { return S_OK; }
    STDMETHOD(JITCachedFunctionSearchStarted)(FunctionID functionId, BOOL* pbUseCachedFunction) { return S_OK; }
    STDMETHOD(JITCachedFunctionSearchFinished)(FunctionID functionId, COR_PRF_JIT_CACHE result) { return S_OK; }
    STDMETHOD(JITFunctionPitched)(FunctionID functionId) { return S_OK; }
    STDMETHOD(JITInlining)(FunctionID callerId, FunctionID calleeId, BOOL* pfShouldInline) { return S_OK; }

    // Thread events - not logged
    STDMETHOD(ThreadCreated)(ThreadID threadId) { return S_OK; }
    STDMETHOD(ThreadDestroyed)(ThreadID threadId) { return S_OK; }
    STDMETHOD(ThreadAssignedToOSThread)(ThreadID managedThreadId, DWORD osThreadId) { return S_OK; }

    // Remoting events - not logged
    STDMETHOD(RemotingClientInvocationStarted)() { return S_OK; }
    STDMETHOD(RemotingClientSendingMessage)(GUID* pCookie, BOOL fIsAsync) { return S_OK; }
    STDMETHOD(RemotingClientReceivingReply)(GUID* pCookie, BOOL fIsAsync) { return S_OK; }
    STDMETHOD(RemotingClientInvocationFinished)() { return S_OK; }
    STDMETHOD(RemotingServerReceivingMessage)(GUID* pCookie, BOOL fIsAsync) { return S_OK; }
    STDMETHOD(RemotingServerInvocationStarted)() { return S_OK; }
    STDMETHOD(RemotingServerInvocationReturned)() { return S_OK; }
    STDMETHOD(RemotingServerSendingReply)(GUID* pCookie, BOOL fIsAsync) { return S_OK; }

    // Transition events - not logged
    STDMETHOD(UnmanagedToManagedTransition)(FunctionID functionId, COR_PRF_TRANSITION_REASON reason) { return S_OK; }
    STDMETHOD(ManagedToUnmanagedTransition)(FunctionID functionId, COR_PRF_TRANSITION_REASON reason) { return S_OK; }

    // Runtime events - not logged
    STDMETHOD(RuntimeSuspendStarted)(COR_PRF_SUSPEND_REASON suspendReason) { return S_OK; }
    STDMETHOD(RuntimeSuspendFinished)() { return S_OK; }
    STDMETHOD(RuntimeSuspendAborted)() { return S_OK; }
    STDMETHOD(RuntimeResumeStarted)() { return S_OK; }
    STDMETHOD(RuntimeResumeFinished)() { return S_OK; }
    STDMETHOD(RuntimeThreadSuspended)(ThreadID threadId) { return S_OK; }
    STDMETHOD(RuntimeThreadResumed)(ThreadID threadId) { return S_OK; }

    // GC events - NOT IMPLEMENTED (removed)
    STDMETHOD(MovedReferences)(ULONG cMovedObjectIDRanges, ObjectID oldObjectIDRangeStart[], ObjectID newObjectIDRangeStart[], ULONG cObjectIDRangeLength[]) { return S_OK; }
    STDMETHOD(ObjectAllocated)(ObjectID objectId, ClassID classId) { return S_OK; }
    STDMETHOD(ObjectsAllocatedByClass)(ULONG cClassCount, ClassID classIds[], ULONG cObjects[]) { return S_OK; }
    STDMETHOD(ObjectReferences)(ObjectID objectId, ClassID classId, ULONG cObjectRefs, ObjectID objectRefIds[]) { return S_OK; }
    STDMETHOD(RootReferences)(ULONG cRootRefs, ObjectID rootRefIds[]) { return S_OK; }

    // Exception events - not logged
    STDMETHOD(ExceptionThrown)(ObjectID thrownObjectId) { return S_OK; }
    STDMETHOD(ExceptionSearchFunctionEnter)(FunctionID functionId) { return S_OK; }
    STDMETHOD(ExceptionSearchFunctionLeave)() { return S_OK; }
    STDMETHOD(ExceptionSearchFilterEnter)(FunctionID functionId) { return S_OK; }
    STDMETHOD(ExceptionSearchFilterLeave)() { return S_OK; }
    STDMETHOD(ExceptionSearchCatcherFound)(FunctionID functionId) { return S_OK; }
    STDMETHOD(ExceptionOSHandlerEnter)(UINT_PTR __unused) { return S_OK; }
    STDMETHOD(ExceptionOSHandlerLeave)(UINT_PTR __unused) { return S_OK; }
    STDMETHOD(ExceptionUnwindFunctionEnter)(FunctionID functionId) { return S_OK; }
    STDMETHOD(ExceptionUnwindFunctionLeave)() { return S_OK; }
    STDMETHOD(ExceptionUnwindFinallyEnter)(FunctionID functionId) { return S_OK; }
    STDMETHOD(ExceptionUnwindFinallyLeave)() { return S_OK; }
    STDMETHOD(ExceptionCatcherEnter)(FunctionID functionId, ObjectID objectId) { return S_OK; }
    STDMETHOD(ExceptionCatcherLeave)() { return S_OK; }

    // COM events - not logged
    STDMETHOD(COMClassicVTableCreated)(ClassID wrappedClassId, REFGUID implementedIID, void* pVTable, ULONG cSlots) { return S_OK; }
    STDMETHOD(COMClassicVTableDestroyed)(ClassID wrappedClassId, REFGUID implementedIID, void* pVTable) { return S_OK; }
    STDMETHOD(ExceptionCLRCatcherFound)() { return S_OK; }
    STDMETHOD(ExceptionCLRCatcherExecute)() { return S_OK; }

    // ICorProfilerCallback2
    STDMETHOD(ThreadNameChanged)(ThreadID threadId, ULONG cchName, WCHAR* name) { return S_OK; }
    STDMETHOD(GarbageCollectionStarted)(int cGenerations, BOOL generationCollected[], COR_PRF_GC_REASON reason) { return S_OK; }
    STDMETHOD(SurvivingReferences)(ULONG cSurvivingObjectIDRanges, ObjectID objectIDRangeStart[], ULONG cObjectIDRangeLength[]) { return S_OK; }
    STDMETHOD(GarbageCollectionFinished)() { return S_OK; }
    STDMETHOD(FinalizeableObjectQueued)(DWORD finalizerFlags, ObjectID objectID) { return S_OK; }
    STDMETHOD(RootReferences2)(ULONG cRootRefs, ObjectID rootRefIds[], COR_PRF_GC_ROOT_KIND rootKinds[], COR_PRF_GC_ROOT_FLAGS rootFlags[], UINT_PTR rootIds[]) { return S_OK; }
    STDMETHOD(HandleCreated)(GCHandleID handleId, ObjectID initialObjectId) { return S_OK; }
    STDMETHOD(HandleDestroyed)(GCHandleID handleId) { return S_OK; }

    // ICorProfilerCallback3
    STDMETHOD(InitializeForAttach)(IUnknown* pICorProfilerInfoUnk, void* pvClientData, UINT cbClientData);
    STDMETHOD(ProfilerAttachComplete)() { return S_OK; }
    STDMETHOD(ProfilerDetachSucceeded)() { return S_OK; }

    // ICorProfilerCallback4
    STDMETHOD(ReJITCompilationStarted)(FunctionID functionId, ReJITID reJitId, BOOL fIsSafeToBlock) { return S_OK; }
    STDMETHOD(GetReJITParameters)(ModuleID moduleId, mdMethodDef methodId, ICorProfilerFunctionControl* pFunctionControl) { return S_OK; }
    STDMETHOD(ReJITCompilationFinished)(FunctionID functionId, ReJITID reJitId, HRESULT hrStatus, BOOL fIsSafeToBlock) { return S_OK; }
    STDMETHOD(ReJITError)(ModuleID moduleId, mdMethodDef methodId, FunctionID functionId, HRESULT hrStatus) { return S_OK; }
    STDMETHOD(MovedReferences2)(ULONG cMovedObjectIDRanges, ObjectID oldObjectIDRangeStart[], ObjectID newObjectIDRangeStart[], SIZE_T cObjectIDRangeLength[]) { return S_OK; }
    STDMETHOD(SurvivingReferences2)(ULONG cSurvivingObjectIDRanges, ObjectID objectIDRangeStart[], SIZE_T cObjectIDRangeLength[]) { return S_OK; }

    // Public method to handle Enter3 callback
    void HandleEnter3(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo);

    // Global singleton instance accessor
    static JitProfilerPlugin* GetInstance() { return s_instance; }
    static void SetInstance(JitProfilerPlugin* instance) { s_instance = instance; }
    static void InitializeMaxRecurseDepth();
private:
    ICorProfilerInfo3* profilerInfo;
    long refCount;
    std::unordered_set<FunctionID> jitLoggedFunctions;
    std::unordered_set<FunctionID> enter3LoggedFunctions;
    std::unordered_set<ModuleID> moduleLoggedFunctions;
    CRITICAL_SECTION jitLock;
    CRITICAL_SECTION enter3Lock;
    CRITICAL_SECTION moduleLock;

    static JitProfilerPlugin* s_instance;

    HANDLE hMapFile;
    volatile int32_t* pSharedFlag;

    static int s_maxRecurseDepth;

    bool IsProfilingEnabled() const;

    TypeArgInfo ResolveTypeArgument(ClassID classId);
    void LogModuleInfo(ModuleID moduleId);
    void LogModuleMappingRecursive(const TypeArgInfo& typeArg, int currentDepth);
    std::wstring FormatTypeArgInfoJson(const TypeArgInfo& typeArg, int currentDepth);
    std::wstring EscapeJson(const std::wstring& str);
};
