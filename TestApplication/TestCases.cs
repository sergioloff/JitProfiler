using System;
using System.Collections.Generic;

namespace JitGenericPlayground
{
    public class TestCases
    {
        public static void RunJit()
        {
            Console.WriteLine("press key XXX");
            Console.ReadKey();
            Console.WriteLine("press key XXX2");
            Console.ReadKey();

            // ------------------------------------------------------------
            // 1. Generic class + static ctor + arrays
            // ------------------------------------------------------------

            var a1 = new MyArrayHolder<int, string>().MakeArray<double>(1, "x", 3.14);

            var a2 = new MyArrayHolder<int, string>().MakeArray<object>(2, "y", "extra");

            var cache0 = MyArrayHolder<int, string>.GetCacheSnapshot();

            var cache1 = MyArrayHolder<string, int>.GetCacheSnapshot();


            // ------------------------------------------------------------
            // 2. Generic methods with in/ref/out and arrays
            // ------------------------------------------------------------

            var handler = new RefOutInHandler<long>();
            int state = 10;
            int[] history;

            long value = 42;
            handler.Process<int>(in value, ref state, out history);

            var value2 = 100L;
            handler.Process<int>(in value2, ref state, out history);


            // ------------------------------------------------------------
            // 3. Static generic factory with constraints and arrays
            // ------------------------------------------------------------

            var ints = ValueFactory<int>.CreateMany(3);

            var dates = ValueFactory<DateTime>.CreateMany(2);


            // ------------------------------------------------------------
            // 4. Nested generics and generic method over nested type
            // ------------------------------------------------------------

            var outer = new Outer<int>();
            var inner = new Outer<int>.Inner<long>();

            var methodResult = inner.DoWork<string>(new[] { 1, 2, 3 }, 5L, "payload");


            // ------------------------------------------------------------
            // 5. Generic interface with variance + arrays
            // ------------------------------------------------------------

            ITransformer<int[], object> t1 = new FirstElementToObject<int>();
            object result1 = t1.Transform(new[] { 1, 2, 3 });

            // ------------------------------------------------------------
            // 6. Combining ref/in/out with constraints and static state
            // ------------------------------------------------------------

            var proc = new ComplexProcessor<MyStruct, MyClass>(2);
            MyStruct val = new MyStruct { X = 123 };
            MyClass st = new MyClass() { X = "initial" };
            MyClass[] log;

            proc.Process2<int>(ref val, ref st, out log);


            Console.WriteLine("press key YYY");
            Console.ReadKey();

            Console.WriteLine(
                "MyArrayHolder<int, string>.MakeArray<double>(System.Int32, System.String, System.Double)");
            Console.WriteLine(
                "MyArrayHolder<int, string>.MakeArray<object>(System.Int32, System.String, System.Object)");
            Console.WriteLine(
                "MyArrayHolder<int, string>.GetCacheSnapshot()");
            Console.WriteLine(
                "MyArrayHolder<string, int>.GetCacheSnapshot()");
            Console.WriteLine(
                "RefOutInHandler<long>.Process<int>(in System.Int64, ref System.Int32, out System.Int32[])");
            Console.WriteLine(
                "ValueFactory<int>.CreateMany(System.Int32)");
            Console.WriteLine(
                "ValueFactory<System.DateTime>.CreateMany(System.Int32)");
            Console.WriteLine(
                "Outer<int>.Inner<long>.DoWork<string>(System.Int32[], System.Int64, System.String)");
            Console.WriteLine(
                "FirstElementToObject<int>.Transform(System.Int32[])");
            Console.WriteLine(
                "ComplexProcessor<MyStruct, string>.Process2<int>(ref MyStruct, ref System.String, out System.String[])");


            Console.WriteLine($"state = {state}, history.Length = {history.Length}");
            Console.WriteLine($"ints.Length = {ints.Length}, dates.Length = {dates.Length}");
            Console.WriteLine($"a1.Length = {a1.Length}, a2.Length = {a2.Length}");
            Console.WriteLine($"cache0.Length = {cache0.Length}, cache1.Length = {cache1.Length}");
            Console.WriteLine($"methodResult.Length = {methodResult.Length}");
            Console.WriteLine($"result1 = {result1}");
            Console.WriteLine($"val.X = {val.X}, st = {st}, log.Length = {log.Length}");
        }
    }

    // ============================================================
    // Declarations
    // ============================================================

    // 1. Generic class + static ctor + arrays
    public class MyArrayHolder<TItem, TMeta>
    {
        // Static generic array field; different per closed TItem,TMeta.
        private static readonly TItem[] _cache;

        // Static constructor runs once per closed generic type MyArrayHolder<,>.
        static MyArrayHolder()
        {
            _cache = new TItem[4];
            if (typeof(TItem).IsValueType)
            {
                // For value types, leave defaults (usually 0 / default(TItem)).
            }
            else
            {
                _cache[0] = default!;
            }
        }

        public TMeta[] MakeArray<TExtra>(TItem item, TMeta meta, TExtra extra)
        {
            // Touch static field to ensure static ctor is considered by the JIT.
            _cache[0] = item;

            // Mix generics + arrays in the return value.
            return new[] { meta };
        }

        public static TItem[] GetCacheSnapshot()
        {
            // Returns the static array, forcing static ctor checks.
            return (TItem[])_cache.Clone();
        }
    }

    // 2. Generic methods with in/ref/out and arrays
    public class RefOutInHandler<TInput>
    {
        // TState constrained to struct to exercise value-type by-ref passing.
        public void Process<TState>(in TInput input,
                                    ref TState state,
                                    out TState[] history)
            where TState : struct
        {
            // Read-only input, by ref (in).
            var localCopyOfInput = input;

            // Mutate the ref parameter.
            state = default;

            // Allocate a generic array and set the out parameter.
            history = new TState[2];
            history[0] = state;

            // Some pointless operation to make sure things aren't optimized out.
            if (!typeof(TState).IsValueType)
                throw new InvalidOperationException("Should never happen for struct");
        }
    }

    // 3. Static generic factory with constraints and arrays
    public static class ValueFactory<T>
        where T : struct
    {
        // Static delegate per closed T.
        public static readonly Func<T> Creator;

        static ValueFactory()
        {
            // For structs, default(T) is always valid.
            Creator = () => default;
        }

        public static T[] CreateMany(int count)
        {
            var result = new T[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = Creator();
            }
            return result;
        }
    }

    // 4. Nested generics and generic method over nested type
    public class Outer<TOuter>
    {
        public class Inner<TInner>
        {
            // Method generic in TMethod, uses various arrays.
            public TMethod[] DoWork<TMethod>(TOuter[] outerArr,
                                             TInner innerValue,
                                             TMethod methodValue)
            {
                // Mix value and reference generic types inside.
                var buffer = new object[3];
                buffer[0] = outerArr.Length;
                buffer[1] = innerValue;
                buffer[2] = methodValue;

                return new[] { methodValue };
            }
        }
    }

    // 5. Generic interface with variance + arrays
    public interface ITransformer<in TIn, out TOut>
    {
        TOut Transform(TIn input);
    }

    public sealed class FirstElementToObject<T> : ITransformer<T[], object>
    {
        public object Transform(T[] input)
        {
            if (input == null || input.Length == 0)
                throw new ArgumentException("Empty array");

            return input[0]!;
        }
    }

    // 6. Combining ref/in/out with constraints and static state
    public struct MyStruct
    {
        public int X;
    }
    public class MyClass
    {
        public string X;
    }

    public class ComplexProcessor<TValue, TState>
        where TValue : struct
        where TState : class, new()
    {
        private static readonly List<TState> _globalStates;
        private int _kaka;
        static ComplexProcessor()
        {
            _globalStates = new List<TState>();
        }
        public ComplexProcessor(int kaka)
        { _kaka = kaka; }

        public unsafe void Process2<TExtra>(ref TValue value,
                                    ref TState state,
                                    out TState[] history)
            where TExtra : unmanaged
        {
            // Touch static generic state.
            _globalStates.Add(state);

            // Simple ref update; visible to caller.
            state = new TState();

            // Create history from static list (array conversion).
            history = _globalStates.ToArray();

            // Some use of TExtra so the JIT has to instantiate this generic.
            int size = sizeof(TExtra) + _kaka;
            if (size == 0)
                throw new InvalidOperationException();
        }
    }
}
