namespace JitLogParser
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;

    public static class MethodBaseSerializer
    {
        public sealed class MethodNode
        {
            public TypeNode DeclaringType { get; set; } = new TypeNode();

            public string Name { get; set; } = string.Empty;

            // Closed generic method type arguments (empty for non-generic methods)
            public List<TypeNode> GenericArguments { get; set; }

            // Method parameter types in order (empty for parameterless methods)
            public List<TypeNode> ParameterTypes { get; set; }

            // True for constructors, false for regular methods
            public bool IsConstructor { get; set; }
            public bool IsStatic { get; set; }
        }

        public static string Serialize(MethodBase method, JsonSerializerOptions options = null)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));

            options ??= new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var node = ToNode(method);
            return JsonSerializer.Serialize(node, options);
        }

        public static MethodBase Deserialize(string json, JsonSerializerOptions options = null)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            options ??= new JsonSerializerOptions();

            var node = JsonSerializer.Deserialize<MethodNode>(json, options)
                       ?? throw new InvalidOperationException("Invalid MethodBase JSON.");

            return FromNode(node);
        }

        public static MethodNode ToNode(MethodBase method)
        {
            var declaringType = method.DeclaringType
                                ?? throw new InvalidOperationException("Method has no declaring type.");

            var declaringTypeNode = TypeSerializer.ToNode(declaringType);

            var genericArgs = new List<TypeNode>();

            if (method is MethodInfo mi && mi.IsGenericMethod && !mi.ContainsGenericParameters)
            {
                foreach (var ga in mi.GetGenericArguments()) // closed generic arguments only
                {
                    genericArgs.Add(TypeSerializer.ToNode(ga));
                }
            }

            var parameterTypes = new List<TypeNode>();
            foreach (var p in method.GetParameters()) // ordered parameters
            {
                parameterTypes.Add(TypeSerializer.ToNode(p.ParameterType));
            }

            return new MethodNode
            {
                DeclaringType = declaringTypeNode,
                Name = method.Name,
                IsConstructor = method.IsConstructor || ((method.MemberType & MemberTypes.Constructor) != 0),
                IsStatic = method.IsStatic,
                GenericArguments = genericArgs,
                ParameterTypes = parameterTypes
            };
        }

        public static MethodBase FromNode(MethodNode node)
        {
            if (node.DeclaringType == null)
                throw new InvalidOperationException("DeclaringType is required.");
            if (string.IsNullOrWhiteSpace(node.Name))
                throw new InvalidOperationException("Name is required.");

            var declaringType = TypeSerializer.FromNode(node.DeclaringType);

            var genericArgTypes = (node.GenericArguments ?? new List<TypeNode>())
                .Select(n => TypeSerializer.FromNode(n))
                .ToArray();

            var parameterTypes = (node.ParameterTypes ?? new List<TypeNode>())
                .Select(n => TypeSerializer.FromNode(n))
                .ToArray();


            if (node.IsConstructor)
            {
                BindingFlags flags;
                if (node.IsStatic)
                {
                    flags =
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Static |
                        BindingFlags.DeclaredOnly;
                }
                else
                {
                    flags =
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | 
                        BindingFlags.DeclaredOnly;
                }
                // Constructors are resolved by parameter types only
                var ctor = declaringType.GetConstructor(
                    flags,
                    binder: null,
                    types: parameterTypes,
                    modifiers: null);

                if (ctor == null)
                {
                    throw new MissingMethodException(
                        $"Could not find constructor on '{declaringType}' with specified signature.");
                }

                return ctor;
            }
            else
            {
                const BindingFlags flags =
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly;
                // Regular methods: match by name, generic arity, and parameter types
                var candidates = declaringType.GetMethods(flags);

                foreach (var m in candidates)
                {
                    if (!string.Equals(m.Name, node.Name, StringComparison.Ordinal))
                        continue;

                    var candidate = m;

                    if (genericArgTypes.Length > 0)
                    {
                        if (!candidate.IsGenericMethodDefinition)
                            continue;

                        if (candidate.GetGenericArguments().Length != genericArgTypes.Length)
                            continue;

                        candidate = candidate.MakeGenericMethod(genericArgTypes);
                    }

                    var candParams = candidate.GetParameters();
                    if (candParams.Length != parameterTypes.Length)
                        continue;

                    bool parametersMatch = true;
                    for (int i = 0; i < candParams.Length; i++)
                    {
                        if (candParams[i].ParameterType != parameterTypes[i])
                        {
                            parametersMatch = false;
                            break;
                        }
                    }

                    if (parametersMatch)
                        return candidate;
                }

                throw new MissingMethodException(
                    $"Could not find method '{node.Name}' on '{declaringType}' with specified signature.");
            }
        }

        // Helpers that reuse TypeSerializer through its public API

    }
}
