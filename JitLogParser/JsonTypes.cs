using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;

namespace JitLogParser
{
    public class TypeNode
    {
        public string Name { get; set; } = string.Empty;
        public string Assembly { get; set; } = string.Empty;
        public List<TypeNode> GenericArguments { get; set; }
    }

    public class JitMessage
    {
        [JsonPropertyName("FunctionID")]
        public ulong FunctionID { get; set; }
    }

    public class ModuleMessage
    {
        [JsonPropertyName("ModuleID")]
        public ulong ModuleID { get; set; }

        [JsonPropertyName("ModuleName")]
        public string ModuleName { get; set; }

        [JsonPropertyName("AssemblyID")]
        public ulong AssemblyID { get; set; }

        [JsonPropertyName("AssemblyName")]
        public string AssemblyName { get; set; }

        // Cached assembly for this module
        public Assembly LoadedAssembly { get; set; }
    }

    public class TypeArgMessage
    {
        [JsonPropertyName("ModuleID")]
        public ulong ModuleID { get; set; }

        [JsonPropertyName("TypeDef")]
        public uint TypeDef { get; set; }

        [JsonPropertyName("NestedCount")]
        public int NestedCount { get; set; }

        [JsonPropertyName("Nested")]
        public List<TypeArgMessage> Nested { get; set; }
    }

    public class Enter3Message
    {
        [JsonPropertyName("FunctionID")]
        public ulong FunctionID { get; set; }

        [JsonPropertyName("ModuleID")]
        public ulong ModuleID { get; set; }

        [JsonPropertyName("MethodToken")]
        public uint MethodToken { get; set; }

        [JsonPropertyName("DeclaringTypeModuleID")]
        public ulong DeclaringTypeModuleID { get; set; }

        [JsonPropertyName("DeclaringTypeToken")]
        public uint DeclaringTypeToken { get; set; }

        [JsonPropertyName("DeclaringTypeArgCount")]
        public int DeclaringTypeArgCount { get; set; }

        [JsonPropertyName("DeclaringTypeArgs")]
        public List<TypeArgMessage> DeclaringTypeArgs { get; set; }

        [JsonPropertyName("MethodTypeArgCount")]
        public int MethodTypeArgCount { get; set; }

        [JsonPropertyName("MethodTypeArgs")]
        public List<TypeArgMessage> MethodTypeArgs { get; set; }
    }
}
