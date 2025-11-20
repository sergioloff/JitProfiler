using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using JitLogParser;

namespace YourNamespace.Tests
{
    [TestFixture]
    public class MethodBaseSerializerTests
    {
        private JsonSerializerOptions _options = null!;

        [SetUp]
        public void SetUp()
        {
            _options = new JsonSerializerOptions
            {
                WriteIndented = false
            };
        }

        [Test]
        public void Serialize_NonGenericInstanceMethod_ProducesExpectedShape()
        {
            // Arrange
            var method = typeof(MethodSample)
                .GetMethod(nameof(MethodSample.InstanceNoArgs),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

            // Act
            var json = MethodBaseSerializer.Serialize(method, _options);
            var node = JsonSerializer.Deserialize<MethodBaseSerializer.MethodNode>(json, _options);

            // Assert
            Assert.IsNotNull(node);
            Assert.AreEqual(typeof(MethodSample).FullName, node!.DeclaringType.Name);
            Assert.AreEqual(typeof(MethodSample).Assembly.GetName().Name, node.DeclaringType.Assembly);
            Assert.AreEqual(nameof(MethodSample.InstanceNoArgs), node.Name);
            Assert.IsFalse(node.IsConstructor);
            Assert.NotNull(node.ParameterTypes);
            Assert.AreEqual(0, node.ParameterTypes!.Count);
            Assert.NotNull(node.GenericArguments);
            Assert.AreEqual(0, node.GenericArguments!.Count);
        }

        [Test]
        public void Roundtrip_NonGenericInstanceMethod_ReturnsEquivalentMethod()
        {
            // Arrange
            var method = typeof(MethodSample)
                .GetMethod(nameof(MethodSample.InstanceWithArgs),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

            // Act
            var json = MethodBaseSerializer.Serialize(method, _options);
            var roundtripped = MethodBaseSerializer.Deserialize(json, _options);

            // Assert
            AssertSameMethod(method, (MethodInfo)roundtripped);
        }

        [Test]
        public void Roundtrip_StaticMethod_ReturnsEquivalentMethod()
        {
            // Arrange
            var method = typeof(MethodSample)
                .GetMethod(nameof(MethodSample.StaticNoArgs),
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;

            // Act
            var json = MethodBaseSerializer.Serialize(method, _options);
            var roundtripped = MethodBaseSerializer.Deserialize(json, _options);

            // Assert
            AssertSameMethod(method, (MethodInfo)roundtripped);
        }

        [Test]
        public void Roundtrip_MethodWithNestedGenericParameter_ReturnsEquivalentMethod()
        {
            // Arrange
            var method = typeof(MethodSample)
                .GetMethod(nameof(MethodSample.MethodWithNestedGeneric),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

            // Act
            var json = MethodBaseSerializer.Serialize(method, _options);
            var roundtripped = MethodBaseSerializer.Deserialize(json, _options);

            // Assert
            AssertSameMethod(method, (MethodInfo)roundtripped);
        }

        [Test]
        public void Roundtrip_ClosedGenericMethod_ReturnsEquivalentMethod()
        {
            // Arrange
            var def = typeof(MethodSample)
                .GetMethod(nameof(MethodSample.GenericWithTypeParam),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

            var constructed = def.MakeGenericMethod(typeof(int));

            // Act
            var json = MethodBaseSerializer.Serialize(constructed, _options);
            var roundtripped = MethodBaseSerializer.Deserialize(json, _options);

            // Assert
            var roundMethod = (MethodInfo)roundtripped;

            Assert.AreEqual(constructed.Name, roundMethod.Name);
            Assert.AreEqual(constructed.DeclaringType, roundMethod.DeclaringType);

            Assert.IsTrue(constructed.IsGenericMethod);
            Assert.IsTrue(roundMethod.IsGenericMethod);

            var origArgs = constructed.GetGenericArguments();
            var roundArgs = roundMethod.GetGenericArguments();
            CollectionAssert.AreEqual(origArgs, roundArgs);

            var origParams = constructed.GetParameters().Select(p => p.ParameterType).ToArray();
            var roundParams = roundMethod.GetParameters().Select(p => p.ParameterType).ToArray();
            CollectionAssert.AreEqual(origParams, roundParams);
        }

        [Test]
        public void Roundtrip_ConstructorWithoutParameters_ReturnsEquivalentConstructor()
        {
            // Arrange
            var ctor = typeof(MethodSample)
                .GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null)!;

            // Act
            var json = MethodBaseSerializer.Serialize(ctor, _options);
            var roundtripped = MethodBaseSerializer.Deserialize(json, _options);

            // Assert
            Assert.IsInstanceOf<ConstructorInfo>(roundtripped);
            var roundCtor = (ConstructorInfo)roundtripped;

            Assert.AreEqual(ctor.DeclaringType, roundCtor.DeclaringType);
            Assert.AreEqual(ctor.GetParameters().Length, roundCtor.GetParameters().Length);
            Assert.IsTrue(roundCtor.IsConstructor);
        }

        [Test]
        public void Roundtrip_ConstructorWithParameters_ReturnsEquivalentConstructor()
        {
            // Arrange
            var ctor = typeof(MethodSample)
                .GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    binder: null,
                    types: new[] { typeof(int) },
                    modifiers: null)!;

            // Act
            var json = MethodBaseSerializer.Serialize(ctor, _options);
            var roundtripped = MethodBaseSerializer.Deserialize(json, _options);

            // Assert
            Assert.IsInstanceOf<ConstructorInfo>(roundtripped);
            var roundCtor = (ConstructorInfo)roundtripped;

            Assert.AreEqual(ctor.DeclaringType, roundCtor.DeclaringType);

            var origParams = ctor.GetParameters().Select(p => p.ParameterType).ToArray();
            var roundParams = roundCtor.GetParameters().Select(p => p.ParameterType).ToArray();
            CollectionAssert.AreEqual(origParams, roundParams);
        }

        [Test]
        public void Serialize_NullMethod_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                MethodBaseSerializer.Serialize(null!, _options);
            });
        }

        [Test]
        public void Deserialize_NullJson_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                MethodBaseSerializer.Deserialize(null!, _options);
            });
        }

        [Test]
        public void Deserialize_InvalidJson_Throws()
        {
            // Missing required fields such as DeclaringType and Name
            const string json = "{}";

            Assert.Throws<InvalidOperationException>(() =>
            {
                MethodBaseSerializer.Deserialize(json, _options);
            });
        }

        [Test]
        public void Deserialize_UnknownMethod_ThrowsMissingMethodException()
        {
            // Arrange: take a valid method JSON and tamper with the name
            var method = typeof(MethodSample)
                .GetMethod(nameof(MethodSample.InstanceNoArgs),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

            var json = MethodBaseSerializer.Serialize(method, _options);
            var tamperedJson = json.Replace(nameof(MethodSample.InstanceNoArgs), "NoSuchMethod");

            // Act & Assert
            Assert.Throws<MissingMethodException>(() =>
            {
                MethodBaseSerializer.Deserialize(tamperedJson, _options);
            });
        }

        private static void AssertSameMethod(MethodInfo expected, MethodInfo actual)
        {
            Assert.AreEqual(expected.Name, actual.Name);
            Assert.AreEqual(expected.DeclaringType, actual.DeclaringType);
            Assert.AreEqual(expected.IsStatic, actual.IsStatic);

            var expectedParams = expected.GetParameters().Select(p => p.ParameterType).ToArray();
            var actualParams = actual.GetParameters().Select(p => p.ParameterType).ToArray();
            CollectionAssert.AreEqual(expectedParams, actualParams);

            Assert.AreEqual(expected.IsGenericMethod, actual.IsGenericMethod);

            if (expected.IsGenericMethod)
            {
                var expectedArgs = expected.GetGenericArguments();
                var actualArgs = actual.GetGenericArguments();
                CollectionAssert.AreEqual(expectedArgs, actualArgs);
            }
        }
    }
}
