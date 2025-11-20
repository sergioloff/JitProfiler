namespace JitLogParser
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Text;

    public static class MethodBaseExtensions
    {
        public static string ToPrettySignature(this MethodBase method)
        {
            var sb = new StringBuilder();

            if (method.DeclaringType != null)
            {
                sb.Append(GetFriendlyTypeName(method.DeclaringType));
                sb.Append('.');
            }

            // Constructors vs normal methods
            if (method is ConstructorInfo)
            {
                if (method.IsStatic)
                    sb.Append("cctor");
                else
                    sb.Append("ctor");
            }
            else
            {
                sb.Append(method.Name);

                // Generic method arguments, e.g. Foo<T1, T2>
                if (method is MethodInfo mi && mi.IsGenericMethod)
                {
                    var genArgs = mi.GetGenericArguments();
                    sb.Append('<');
                    sb.Append(string.Join(", ", genArgs.Select(GetFriendlyTypeName)));
                    sb.Append('>');
                }
            }

            // Parameters
            sb.Append('(');
            var parameters = method.GetParameters();
            sb.Append(string.Join(", ", parameters.Select(FormatParameter)));
            sb.Append(')');

            return sb.ToString();
        }

        private static string FormatParameter(ParameterInfo p)
        {
            var type = p.ParameterType;
            string modifier = string.Empty;

            // params
            if (Attribute.IsDefined(p, typeof(ParamArrayAttribute)))
            {
                modifier = "params ";
                // params T[] x  -> show as "params T[]"
                var elemType = type.GetElementType() ?? type;
                return $"{modifier}{GetFriendlyTypeName(elemType)}[] {p.Name}";
            }

            // ref / out
            if (type.IsByRef)
            {
                type = type.GetElementType()!;
                modifier = p.IsOut ? "out " : "ref ";
            }

            return $"{modifier}{GetFriendlyTypeName(type)} {p.Name}";
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type.IsGenericParameter)
                return type.Name;

            if (type.IsArray)
            {
                var elem = GetFriendlyTypeName(type.GetElementType()!);
                var rank = type.GetArrayRank();
                return rank == 1
                    ? $"{elem}[]"
                    : $"{elem}[{new string(',', rank - 1)}]";
            }

            if (type.IsPointer)
                return GetFriendlyTypeName(type.GetElementType()!) + "*";

            if (type.IsByRef)
                return GetFriendlyTypeName(type.GetElementType()!) + "&";

            if (type.IsGenericType)
            {
                var name = StripGenericArity(type.Name);
                var args = type.GetGenericArguments()
                               .Select(GetFriendlyTypeName);
                var genericSuffix = "<" + string.Join(", ", args) + ">";

                if (type.IsNested && type.DeclaringType != null)
                    return GetFriendlyTypeName(type.DeclaringType) +
                           "." + name + genericSuffix;

                return name + genericSuffix;
            }

            if (type.IsNested && type.DeclaringType != null)
                return GetFriendlyTypeName(type.DeclaringType) +
                       "." + type.Name;

            return type.Name;
        }
        private static string StripGenericArity(string name)
        {
            var tickIndex = name.IndexOf('`');
            return tickIndex >= 0 ? name[..tickIndex] : name;
        }
    }
}
