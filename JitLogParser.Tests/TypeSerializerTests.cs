using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using JitLogParser;

namespace YourNamespace.Tests
{

    [TestFixture]
    public class TypeSerializerTests
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
        public void Serialize_NonGenericType_ProducesExpectedJson()
        {
            // Arrange
            var type = typeof(string);

            // Act
            var json = TypeSerializer.Serialize(type, _options);
            var node = JsonSerializer.Deserialize<TypeNode>(json, _options);

            // Assert
            Assert.IsNotNull(node);
            Assert.AreEqual(typeof(string).FullName, node!.Name);
            Assert.AreEqual(typeof(string).Assembly.GetName().Name, node.Assembly);
            Assert.IsNull(node.GenericArguments);
        }

        [Test]
        public void Roundtrip_NonGenericType_ReturnsSameType()
        {
            // Arrange
            var type = typeof(int);

            // Act
            var json = TypeSerializer.Serialize(type, _options);
            var roundtripped = TypeSerializer.Deserialize(json, _options);

            // Assert
            Assert.AreEqual(type, roundtripped);
        }

        [Test]
        public void Serialize_GenericTypeDefinition_HasNoGenericArguments()
        {
            // Arrange
            var type = typeof(List<>);

            // Act
            var json = TypeSerializer.Serialize(type, _options);
            var node = JsonSerializer.Deserialize<TypeNode>(json, _options);

            // Assert
            Assert.IsNotNull(node);
            Assert.AreEqual(type.FullName, node!.Name);
            Assert.AreEqual(type.Assembly.GetName().Name, node.Assembly);
            Assert.IsNull(node.GenericArguments);
        }

        [Test]
        public void Roundtrip_GenericTypeDefinition_ReturnsSameType()
        {
            // Arrange
            var type = typeof(Dictionary<,>);

            // Act
            var json = TypeSerializer.Serialize(type, _options);
            var roundtripped = TypeSerializer.Deserialize(json, _options);

            // Assert
            Assert.AreEqual(type, roundtripped);
        }

        [Test]
        public void Serialize_ClosedGenericType_PopulatesGenericArguments()
        {
            // Arrange
            var type = typeof(Dictionary<string, int>);

            // Act
            var json = TypeSerializer.Serialize(type, _options);
            var node = JsonSerializer.Deserialize<TypeNode>(json, _options);

            // Assert
            Assert.IsNotNull(node);
            Assert.AreEqual(type.GetGenericTypeDefinition().FullName, node!.Name);
            Assert.AreEqual(type.GetGenericTypeDefinition().Assembly.GetName().Name, node.Assembly);
            Assert.IsNotNull(node.GenericArguments);
            Assert.AreEqual(2, node.GenericArguments!.Count);

            var arg0 = node.GenericArguments[0];
            var arg1 = node.GenericArguments[1];

            Assert.AreEqual(typeof(string).FullName, arg0.Name);
            Assert.AreEqual(typeof(string).Assembly.GetName().Name, arg0.Assembly);
            Assert.IsNull(arg0.GenericArguments);

            Assert.AreEqual(typeof(int).FullName, arg1.Name);
            Assert.AreEqual(typeof(int).Assembly.GetName().Name, arg1.Assembly);
            Assert.IsNull(arg1.GenericArguments);
        }

        [Test]
        public void Roundtrip_ClosedGenericType_ReturnsSameConstructedType()
        {
            // Arrange
            var type = typeof(Dictionary<string, int>);

            // Act
            var json = TypeSerializer.Serialize(type, _options);
            var roundtripped = TypeSerializer.Deserialize(json, _options);

            // Assert
            Assert.AreEqual(type, roundtripped);
            Assert.IsTrue(roundtripped.IsGenericType);
            Assert.AreEqual(type.GetGenericTypeDefinition(), roundtripped.GetGenericTypeDefinition());
            CollectionAssert.AreEqual(type.GetGenericArguments(), roundtripped.GetGenericArguments());
        }

        private class SampleGeneric<T>
        {
        }

        [Test]
        public void Roundtrip_NestedClosedGenericType_ReturnsSameConstructedType()
        {
            // Arrange
            var type = typeof(Dictionary<string, List<SampleGeneric<int>>>);

            // Act
            var json = TypeSerializer.Serialize(type, _options);
            var roundtripped = TypeSerializer.Deserialize(json, _options);

            // Assert
            Assert.AreEqual(type, roundtripped);
            Assert.IsTrue(roundtripped.IsGenericType);

            var args = roundtripped.GetGenericArguments();
            Assert.AreEqual(2, args.Length);
            Assert.AreEqual(typeof(string), args[0]);

            var innerList = args[1];
            Assert.AreEqual(typeof(List<SampleGeneric<int>>), innerList);

            var listArg = innerList.GetGenericArguments()[0];
            Assert.AreEqual(typeof(SampleGeneric<int>), listArg);

            var innermostArg = listArg.GetGenericArguments()[0];
            Assert.AreEqual(typeof(int), innermostArg);
        }

        [Test]
        public void Serialize_UsesSimpleAssemblyNamesOnly()
        {
            // Arrange
            var type = typeof(Dictionary<string, int>);

            // Act
            var json = TypeSerializer.Serialize(type, _options);
            var node = JsonSerializer.Deserialize<TypeNode>(json, _options);

            // Assert
            Assert.IsNotNull(node);
            Assert.AreEqual(type.GetGenericTypeDefinition().Assembly.GetName().Name, node!.Assembly);

            Assert.IsNotNull(node.GenericArguments);
            foreach (var arg in node.GenericArguments!)
            {
                var asmName = new AssemblyName(arg.Assembly);
                Assert.IsNotNull(asmName.Name);
                Assert.IsNull(asmName.Version);
            }
        }

        [Test]
        public void Deserialize_InvalidJson_Throws()
        {
            // Arrange
            const string json = "{}"; // Missing Name and Assembly

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
            {
                TypeSerializer.Deserialize(json, _options);
            });
        }

        [Test]
        public void Serialize_NullType_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                TypeSerializer.Serialize(null!, _options);
            });
        }

        [Test]
        public void Deserialize_NullJson_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                TypeSerializer.Deserialize(null!, _options);
            });
        }
    }
}
