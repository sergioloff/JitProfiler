#pragma once
#include <cor.h>
#include <corprof.h>
#include <corhlpr.h>
#include <windows.h>
#include <stdio.h>
#include <vector>
#include <unordered_set>
#include <string>

// Forward declare the global Enter3 callback function with correct signature
void __stdcall GlobalEnter3Callback(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo);

// Helper structure to represent type argument info
struct TypeArgInfo {
	ModuleID moduleId;
	mdTypeDef typeDef;
	std::vector<TypeArgInfo> nestedTypeArgs;
};

// Thread-safe logging helper with per-file logging
class ProfilerLogger
{
public:
	static void LogJIT(const wchar_t* format, ...)
	{
		if (!format) return;
		EnterCriticalSection(&g_jitLogLock);
		va_list args;
		va_start(args, format);
		wchar_t path[512];
		GetLogPath(L"jit.json", path, sizeof(path) / sizeof(wchar_t));
		FILE* f = nullptr;
		errno_t err = _wfopen_s(&f, path, L"a");
		if (f && err == 0)
		{
			vfwprintf(f, format, args);
			fwprintf(f, L"\n");
			fflush(f);
			fclose(f);
		}
		va_end(args);
		LeaveCriticalSection(&g_jitLogLock);
	}

	static void LogEnter3(const wchar_t* format, ...)
	{
		if (!format) return;
		EnterCriticalSection(&g_enter3LogLock);
		va_list args;
		va_start(args, format);
		wchar_t path[512];
		GetLogPath(L"enter3.json", path, sizeof(path) / sizeof(wchar_t));
		FILE* f = nullptr;
		errno_t err = _wfopen_s(&f, path, L"a");
		if (f && err == 0)
		{
			vfwprintf(f, format, args);
			fwprintf(f, L"\n");
			fflush(f);
			fclose(f);
		}
		va_end(args);
		LeaveCriticalSection(&g_enter3LogLock);
	}

	static void LogModule(const wchar_t* format, ...)
	{
		if (!format) return;
		EnterCriticalSection(&g_moduleLogLock);
		va_list args;
		va_start(args, format);
		wchar_t path[512];
		GetLogPath(L"modules.json", path, sizeof(path) / sizeof(wchar_t));
		FILE* f = nullptr;
		errno_t err = _wfopen_s(&f, path, L"a");
		if (f && err == 0)
		{
			vfwprintf(f, format, args);
			fwprintf(f, L"\n");
			fflush(f);
			fclose(f);
		}
		va_end(args);
		LeaveCriticalSection(&g_moduleLogLock);
	}

	static void Initialize()
	{
		if (!g_initialized)
		{
			InitializeCriticalSection(&g_jitLogLock);
			InitializeCriticalSection(&g_enter3LogLock);
			InitializeCriticalSection(&g_moduleLogLock);
			g_initialized = true;
		}
	}

private:
	static void GetLogPath(const wchar_t* filename, wchar_t* outPath, size_t maxLen)
	{
		wchar_t basePath[512] = L"C:\\siglocal";
		wchar_t envPath[512];
		if (GetEnvironmentVariableW(L"SIG_JIT_PROFILER_LOG_PATH", envPath, sizeof(envPath) / sizeof(wchar_t)) > 0)
		{
			wcscpy_s(basePath, sizeof(basePath) / sizeof(wchar_t), envPath);
		}

		swprintf_s(outPath, maxLen, L"%s\\%s", basePath, filename);
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

	// IUnknown interface implementation
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

	// Memory-mapped file for IPC flag
	HANDLE hMapFile;
	volatile int32_t* pSharedFlag;

	// Helper method to check if profiling is enabled
	bool IsProfilingEnabled() const;

	// Helper methods for HandleEnter3
	TypeArgInfo ResolveTypeArgument(ClassID classId);
	void LogModuleInfo(ModuleID moduleId);
	void LogModuleMappingRecursive(const TypeArgInfo& typeArg);
	std::wstring FormatTypeArgInfoJson(const TypeArgInfo& typeArg);
	std::wstring EscapeJson(const std::wstring& str);
};
