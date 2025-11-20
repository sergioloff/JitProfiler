#include "JitProfilerPlugin.h"

// Initialize static members
CRITICAL_SECTION ProfilerLogger::g_jitLogLock;
CRITICAL_SECTION ProfilerLogger::g_enter3LogLock;
CRITICAL_SECTION ProfilerLogger::g_moduleLogLock;
bool ProfilerLogger::g_initialized = false;

JitProfilerPlugin* JitProfilerPlugin::s_instance = nullptr;

// Global callback function for Enter3 - matches FunctionEnter3 signature
void __stdcall GlobalEnter3Callback(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo)
{
    JitProfilerPlugin* instance = JitProfilerPlugin::GetInstance();
    if (instance != nullptr)
    {
        instance->HandleEnter3(functionIDOrClientID, eltInfo);
    }
}

JitProfilerPlugin::JitProfilerPlugin()
    : profilerInfo(NULL), refCount(1), hMapFile(NULL), pSharedFlag(nullptr)
{
    InitializeCriticalSection(&jitLock);
    InitializeCriticalSection(&enter3Lock);
    InitializeCriticalSection(&moduleLock);
    SetInstance(this);
}

JitProfilerPlugin::~JitProfilerPlugin()
{
    DeleteCriticalSection(&jitLock);
    DeleteCriticalSection(&enter3Lock);
    DeleteCriticalSection(&moduleLock);

    if (profilerInfo != NULL)
    {
        profilerInfo->Release();
        profilerInfo = NULL;
    }

    // Clean up memory-mapped file
    if (pSharedFlag != nullptr)
    {
        UnmapViewOfFile((LPCVOID)pSharedFlag);
        pSharedFlag = nullptr;
    }
    if (hMapFile != NULL)
    {
        CloseHandle(hMapFile);
        hMapFile = NULL;
    }

    SetInstance(nullptr);
}

bool JitProfilerPlugin::IsProfilingEnabled() const
{
    // If no shared flag is mapped, default to enabled
    if (pSharedFlag == nullptr)
        return true;

    // Check if the flag is non-zero (enabled)
    return (*pSharedFlag != 0);
}

HRESULT STDMETHODCALLTYPE JitProfilerPlugin::Initialize(IUnknown* pICorProfilerInfoUnk)
{
    return InitializeForAttach(pICorProfilerInfoUnk, NULL, 0);
}

HRESULT STDMETHODCALLTYPE JitProfilerPlugin::Shutdown()
{
    if (profilerInfo != NULL)
    {
        profilerInfo->Release();
        profilerInfo = NULL;
    }
    return S_OK;
}

HRESULT STDMETHODCALLTYPE JitProfilerPlugin::InitializeForAttach(
    IUnknown* pICorProfilerInfoUnk,
    void* pvClientData,
    UINT cbClientData)
{
    if (pICorProfilerInfoUnk == NULL)
        return E_INVALIDARG;

    HRESULT hr = pICorProfilerInfoUnk->QueryInterface(
        __uuidof(ICorProfilerInfo3),
        (void**)&profilerInfo);

    if (FAILED(hr))
        return hr;

    // Set event mask to receive JIT and Enter/Leave events
    DWORD eventMask =
        COR_PRF_MONITOR_JIT_COMPILATION |
        COR_PRF_MONITOR_ENTERLEAVE |
        COR_PRF_ENABLE_FRAME_INFO;

    hr = profilerInfo->SetEventMask(eventMask);
    if (FAILED(hr))
    {
        profilerInfo->Release();
        profilerInfo = NULL;
        return hr;
    }

    hr = profilerInfo->SetEnterLeaveFunctionHooks3WithInfo(GlobalEnter3Callback, NULL, NULL);
    if (FAILED(hr))
    {
        profilerInfo->Release();
        profilerInfo = NULL;
        return hr;
    }

    // Open memory-mapped file from environment variable
    wchar_t mapName[512];
    DWORD envLen = GetEnvironmentVariableW(L"SIG_JIT_PROFILER_MAP_ID", mapName, sizeof(mapName) / sizeof(wchar_t));
    if (envLen == 0)
    {
        wcscpy_s(mapName, sizeof(mapName) / sizeof(wchar_t), L"SIG_JITPROFILER");
    }

    hMapFile = OpenFileMappingW(FILE_MAP_READ, FALSE, mapName);
    if (hMapFile != NULL)
    {
        pSharedFlag = (volatile int32_t*)MapViewOfFile(hMapFile, FILE_MAP_READ, 0, 0, sizeof(int32_t));
        if (pSharedFlag == NULL)
        {
            CloseHandle(hMapFile);
            hMapFile = NULL;
        }
    }

    return S_OK;
}

HRESULT STDMETHODCALLTYPE JitProfilerPlugin::JITCompilationStarted(FunctionID functionId, BOOL fIsSafeToBlock)
{
    if (!IsProfilingEnabled())
        return S_OK;

    EnterCriticalSection(&jitLock);
    if (jitLoggedFunctions.find(functionId) != jitLoggedFunctions.end())
    {
        LeaveCriticalSection(&jitLock);
        return S_OK;
    }
    jitLoggedFunctions.insert(functionId);
    LeaveCriticalSection(&jitLock);

    // Log as JSON with numeric value
    ProfilerLogger::LogJIT(L"{\"FunctionID\":%llu}", (unsigned long long)functionId);
    return S_OK;
}

std::wstring JitProfilerPlugin::EscapeJson(const std::wstring& str)
{
    std::wstring result;
    result.reserve(str.length());
    for (wchar_t c : str)
    {
        switch (c)
        {
        case L'\"': result += L"\\\""; break;
        case L'\\': result += L"\\\\"; break;
        case L'\b': result += L"\\b"; break;
        case L'\f': result += L"\\f"; break;
        case L'\n': result += L"\\n"; break;
        case L'\r': result += L"\\r"; break;
        case L'\t': result += L"\\t"; break;
        default: result += c; break;
        }
    }
    return result;
}

TypeArgInfo JitProfilerPlugin::ResolveTypeArgument(ClassID classId)
{
    TypeArgInfo info;
    ModuleID moduleId;
    mdTypeDef typeDef;
    ClassID parentClassId;
    ULONG32 typeArgCount = 0;

    HRESULT hr = profilerInfo->GetClassIDInfo2(
        classId,
        &moduleId,
        &typeDef,
        &parentClassId,
        0,
        &typeArgCount,
        nullptr);

    if (FAILED(hr))
        return info;

    info.moduleId = moduleId;
    info.typeDef = typeDef;

    if (typeArgCount > 0)
    {
        std::vector<ClassID> typeArgs(typeArgCount);
        hr = profilerInfo->GetClassIDInfo2(
            classId,
            &moduleId,
            &typeDef,
            &parentClassId,
            typeArgCount,
            &typeArgCount,
            typeArgs.data());

        if (SUCCEEDED(hr))
        {
            for (ULONG32 i = 0; i < typeArgCount; i++)
            {
                info.nestedTypeArgs.push_back(ResolveTypeArgument(typeArgs[i]));
            }
        }
    }

    return info;
}

void JitProfilerPlugin::LogModuleInfo(ModuleID moduleId)
{
    EnterCriticalSection(&moduleLock);
    if (moduleLoggedFunctions.find(moduleId) != moduleLoggedFunctions.end())
    {
        LeaveCriticalSection(&moduleLock);
        return;
    }
    moduleLoggedFunctions.insert(moduleId);
    LeaveCriticalSection(&moduleLock);

    WCHAR moduleName[512];
    ULONG moduleNameLen = 0;
    AssemblyID assemblyId;
    LPCBYTE baseLoadAddress;

    HRESULT hr = profilerInfo->GetModuleInfo(
        moduleId,
        &baseLoadAddress,
        512,
        &moduleNameLen,
        moduleName,
        &assemblyId
    );

    if (FAILED(hr))
        return;

    WCHAR assemblyName[512];
    ULONG assemblyNameLen = 0;
    AppDomainID appDomainId;
    ModuleID manifestModuleId;

    hr = profilerInfo->GetAssemblyInfo(
        assemblyId,
        512,
        &assemblyNameLen,
        assemblyName,
        &appDomainId,
        &manifestModuleId
    );

    if (SUCCEEDED(hr))
    {
        std::wstring escapedModuleName = EscapeJson(moduleName);
        std::wstring escapedAssemblyName = EscapeJson(assemblyName);

        // Log as JSON with numeric values for IDs
        ProfilerLogger::LogModule(
            L"{\"ModuleID\":%llu,\"ModuleName\":\"%s\",\"AssemblyID\":%llu,\"AssemblyName\":\"%s\"}",
            (unsigned long long)moduleId, escapedModuleName.c_str(),
            (unsigned long long)assemblyId, escapedAssemblyName.c_str());
    }
}

void JitProfilerPlugin::LogModuleMappingRecursive(const TypeArgInfo& typeArg)
{
    LogModuleInfo(typeArg.moduleId);
    for (const auto& nested : typeArg.nestedTypeArgs)
    {
        LogModuleMappingRecursive(nested);
    }
}

std::wstring JitProfilerPlugin::FormatTypeArgInfoJson(const TypeArgInfo& typeArg)
{
    wchar_t buffer[512];
    // Use numeric values instead of hex strings
    swprintf_s(buffer, 512, L"{\"ModuleID\":%llu,\"TypeDef\":%u,\"NestedCount\":%u",
        (unsigned long long)typeArg.moduleId, typeArg.typeDef, (unsigned int)typeArg.nestedTypeArgs.size());

    std::wstring result = buffer;

    if (!typeArg.nestedTypeArgs.empty())
    {
        result += L",\"Nested\":[";
        for (size_t i = 0; i < typeArg.nestedTypeArgs.size(); i++)
        {
            if (i > 0) result += L",";
            result += FormatTypeArgInfoJson(typeArg.nestedTypeArgs[i]);
        }
        result += L"]";
    }

    result += L"}";
    return result;
}

void JitProfilerPlugin::HandleEnter3(FunctionIDOrClientID functionIDOrClientID, COR_PRF_ELT_INFO eltInfo)
{
    if (!IsProfilingEnabled())
        return;

    FunctionID functionId = functionIDOrClientID.functionID;

    EnterCriticalSection(&enter3Lock);
    if (enter3LoggedFunctions.find(functionId) != enter3LoggedFunctions.end())
    {
        LeaveCriticalSection(&enter3Lock);
        return;
    }
    enter3LoggedFunctions.insert(functionId);
    LeaveCriticalSection(&enter3Lock);

    COR_PRF_FRAME_INFO frameInfo = 0;
    HRESULT hr = profilerInfo->GetFunctionEnter3Info(functionId, eltInfo, &frameInfo, nullptr, nullptr);
    if (FAILED(hr))
        frameInfo = 0;

    ClassID classId;
    ModuleID moduleId;
    mdToken methodToken;
    ULONG32 methodTypeArgCount = 0;

    hr = profilerInfo->GetFunctionInfo2(
        functionId,
        frameInfo,
        &classId,
        &moduleId,
        &methodToken,
        0,
        &methodTypeArgCount,
        nullptr);

    if (FAILED(hr))
        return;

    std::vector<ClassID> methodTypeArgs(methodTypeArgCount);
    if (methodTypeArgCount > 0)
    {
        hr = profilerInfo->GetFunctionInfo2(
            functionId,
            frameInfo,
            &classId,
            &moduleId,
            &methodToken,
            methodTypeArgCount,
            &methodTypeArgCount,
            methodTypeArgs.data());

        if (FAILED(hr))
            return;
    }

    ModuleID typeModuleId = 0;
    mdTypeDef typeDefToken = 0;
    ClassID parentClassId = 0;
    ULONG32 declaringTypeArgCount = 0;
    std::vector<ClassID> declaringTypeArgs;

    if (classId != 0)
    {
        hr = profilerInfo->GetClassIDInfo2(
            classId,
            &typeModuleId,
            &typeDefToken,
            &parentClassId,
            0,
            &declaringTypeArgCount,
            nullptr);

        if (SUCCEEDED(hr) && declaringTypeArgCount > 0)
        {
            declaringTypeArgs.resize(declaringTypeArgCount);
            hr = profilerInfo->GetClassIDInfo2(
                classId,
                &typeModuleId,
                &typeDefToken,
                &parentClassId,
                declaringTypeArgCount,
                &declaringTypeArgCount,
                declaringTypeArgs.data());

            if (FAILED(hr))
            {
                declaringTypeArgs.clear();
                declaringTypeArgCount = 0;
            }
        }
    }

    std::vector<TypeArgInfo> resolvedDeclaringTypeArgs;
    for (ULONG32 i = 0; i < declaringTypeArgCount; i++)
    {
        resolvedDeclaringTypeArgs.push_back(ResolveTypeArgument(declaringTypeArgs[i]));
    }

    std::vector<TypeArgInfo> resolvedMethodTypeArgs;
    for (ULONG32 i = 0; i < methodTypeArgCount; i++)
    {
        resolvedMethodTypeArgs.push_back(ResolveTypeArgument(methodTypeArgs[i]));
    }

    LogModuleInfo(moduleId);
    if (typeModuleId != 0)
    {
        LogModuleInfo(typeModuleId);
    }

    for (const auto& typeArg : resolvedDeclaringTypeArgs)
    {
        LogModuleMappingRecursive(typeArg);
    }

    for (const auto& typeArg : resolvedMethodTypeArgs)
    {
        LogModuleMappingRecursive(typeArg);
    }

    // Build JSON string with numeric values
    wchar_t buffer[256];
    std::wstring json = L"{";

    swprintf_s(buffer, 256, L"\"FunctionID\":%llu", (unsigned long long)functionId);
    json += buffer;

    swprintf_s(buffer, 256, L",\"ModuleID\":%llu", (unsigned long long)moduleId);
    json += buffer;

    swprintf_s(buffer, 256, L",\"MethodToken\":%u", methodToken);
    json += buffer;

    swprintf_s(buffer, 256, L",\"DeclaringTypeModuleID\":%llu", (unsigned long long)typeModuleId);
    json += buffer;

    swprintf_s(buffer, 256, L",\"DeclaringTypeToken\":%u", typeDefToken);
    json += buffer;

    swprintf_s(buffer, 256, L",\"DeclaringTypeArgCount\":%u", declaringTypeArgCount);
    json += buffer;

    if (!resolvedDeclaringTypeArgs.empty())
    {
        json += L",\"DeclaringTypeArgs\":[";
        for (size_t i = 0; i < resolvedDeclaringTypeArgs.size(); i++)
        {
            if (i > 0) json += L",";
            json += FormatTypeArgInfoJson(resolvedDeclaringTypeArgs[i]);
        }
        json += L"]";
    }

    swprintf_s(buffer, 256, L",\"MethodTypeArgCount\":%u", methodTypeArgCount);
    json += buffer;

    if (!resolvedMethodTypeArgs.empty())
    {
        json += L",\"MethodTypeArgs\":[";
        for (size_t i = 0; i < resolvedMethodTypeArgs.size(); i++)
        {
            if (i > 0) json += L",";
            json += FormatTypeArgInfoJson(resolvedMethodTypeArgs[i]);
        }
        json += L"]";
    }

    json += L"}";

    ProfilerLogger::LogEnter3(L"%s", json.c_str());
}
