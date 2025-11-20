using System.Collections.Generic;

namespace YourNamespace.Tests
{
    public class MethodSample
    {
        public MethodSample() { }

        public MethodSample(int value) { }

        public void InstanceNoArgs() { }

        public int InstanceWithArgs(string s, int i) => i;

        public static void StaticNoArgs() { }

        public void MethodWithNestedGeneric(Dictionary<string, List<int>> value) { }

        public TResult GenericMethod<TResult>(TResult value) => value;

        public void GenericWithTypeParam<T>(T value) { }
    }
}
