namespace JitLogParser
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;

    public static class TypeSerializer
    {

        /// <summary>
        /// Serialize a System.Type into a JSON string using a version-agnostic representation.
        /// </summary>
        public static string Serialize(Type type, JsonSerializerOptions options = null)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            var node = ToNode(type);
            options ??= new JsonSerializerOptions
            {
                WriteIndented = true
            };

            return JsonSerializer.Serialize(node, options);
        }

        /// <summary>
        /// Deserialize a JSON string (created by Serialize) back into a System.Type.
        /// </summary>
        public static Type Deserialize(string json, JsonSerializerOptions options = null)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            options ??= new JsonSerializerOptions();
            var node = JsonSerializer.Deserialize<TypeNode>(json, options)
                       ?? throw new InvalidOperationException("Invalid type JSON.");

            return FromNode(node);
        }

        // Build a TypeNode from a System.Type (handles nested closed generics).
        public static TypeNode ToNode(Type type)
        {
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                // Use the generic type definition for the outer type
                var genericDef = type.GetGenericTypeDefinition();

                return new TypeNode
                {
                    Name = genericDef.FullName ?? genericDef.Name,
                    Assembly = genericDef.Assembly.GetName().Name ?? genericDef.Assembly.FullName!,
                    GenericArguments = type
                        .GetGenericArguments()
                        .Select(ToNode)
                        .ToList()
                };
            }

            if (type.IsGenericTypeDefinition)
            {
                return new TypeNode
                {
                    Name = type.FullName ?? type.Name,
                    Assembly = type.Assembly.GetName().Name ?? type.Assembly.FullName!,
                    GenericArguments = null
                };
            }

            // Non-generic type
            return new TypeNode
            {
                Name = type.FullName ?? type.Name,
                Assembly = type.Assembly.GetName().Name ?? type.Assembly.FullName!,
                GenericArguments = null
            };
        }

        // Rebuild a System.Type from a TypeNode (handles nested closed generics).
        public static Type FromNode(TypeNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (string.IsNullOrWhiteSpace(node.Name))
                throw new InvalidOperationException("TypeNode.Name must be set.");
            if (string.IsNullOrWhiteSpace(node.Assembly))
                throw new InvalidOperationException("TypeNode.Assembly must be set.");

            // Load by simple assembly name; CLR / binding redirects take care of version selection.
            var assemblyName = new AssemblyName(node.Assembly);
            var assembly = Assembly.Load(assemblyName);

            var type = assembly.GetType(node.Name, throwOnError: true)
                       ?? throw new TypeLoadException(
                           $"Could not load type '{node.Name}' from assembly '{node.Assembly}'.");

            var args = node.GenericArguments;

            if (args is { Count: > 0 })
            {
                if (!type.IsGenericTypeDefinition)
                {
                    // In case the loaded type was already closed for some reason, normalize.
                    type = type.GetGenericTypeDefinition();
                }

                var typeArgs = args.Select(FromNode).ToArray();
                type = type.MakeGenericType(typeArgs);
            }

            return type;
        }
    }
}
