namespace TestApplication
{
    using JitGenericPlayground;
    using JitLogParser;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        static void Main(string[] args)
        {
            Prime(@"C:\siglocal\JitProfilerPlugin\OLD_jitManifest.json", out int totalLoaded, out int totalPrimed, out string errors);
            Console.WriteLine($"totalLoaded={totalLoaded}, totalPrimed={totalPrimed}");
            if (!string.IsNullOrEmpty(errors))
                Console.WriteLine($"{errors}");


            //var methodDef = typeof(MyClass1<int>).GetMethod("MyMethod");
            //var method = methodDef.MakeGenericMethod(typeof(long));
            //var node = MethodBaseSerializer.Serialize(method);



            Console.WriteLine("press key 1");
            Console.ReadKey();
            Console.WriteLine("press key 2");
            Console.ReadKey();

            TestCases.RunJit();
            //new MyClass1<int>().MyMethod<long>(1, 2L);



            Console.WriteLine("press key 3");
            Console.ReadKey();

            Test();


            Console.ReadKey();
        }

        public static void Test()
        {
            string folder = @"C:\siglocal\JitProfilerPlugin";
            var tx = File.ReadAllText(System.IO.Path.Combine(folder, "jitManifest.json"));
            var nodes = JsonSerializer.Deserialize<MethodBaseSerializer.MethodNode[]>(tx, new JsonSerializerOptions());
            var methodBaseList = nodes.Select(x => MethodBaseSerializer.FromNode(x)).ToList();
            string Text = string.Join("\r\n", methodBaseList.Select(x => x.ToPrettySignature()));
            Console.WriteLine(Text);
        }
        public static void Prime(string manifestFile, out int totalLoaded, out int totalPrimed, out string errors)
        {
            totalLoaded = 0;
            totalPrimed = 0;
            errors = null;
            if (!File.Exists(manifestFile))
            {
                errors = "File not found: " + manifestFile;
                return;
            }
            List<MethodBase> methodBaseList = null;
            try
            {
                var tx = File.ReadAllText(manifestFile);
                var nodes = JsonSerializer.Deserialize<MethodBaseSerializer.MethodNode[]>(tx, new JsonSerializerOptions());
                totalLoaded = nodes.Length;
                methodBaseList = nodes.Select(x => MethodBaseSerializer.FromNode(x)).ToList();
            }
            catch (Exception ex)
            {
                errors += $"{ex.Message}\r\n";
                return;
            }
            foreach (var m in methodBaseList)
            {
                if (m != null)
                {
                    try
                    {
                        RuntimeHelpers.PrepareMethod(m.MethodHandle);
                        totalPrimed++;
                    }
                    catch (Exception ex2)
                    {
                        errors += $"Failed to prime {m.Name}: {ex2.Message}\r\n";
                    }
                }
                else
                    errors += $"Failed to prime {m.Name}: unresolved\r\n";
            }
        }
    }


    public class MyClass1<T1>
    {
        public void MyMethod<T2>(T1 arg1, T2 arg2) { return; }
    }
}
