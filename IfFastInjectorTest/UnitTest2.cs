using System;
using NUnit.Framework;
using IfFastInjector;
using IfFastInjector.IfInjectorTypes;

namespace IfFastInjectorMxTest
{
    [TestFixture]
    public class UnitTest2
    {
		private IfInjector injector = IfInjector.NewInstance();

        class TestClassSetResolverByConstructorInfoTest
        {
            public TestClassSetResolverByConstructorInfoTest() { }
            public TestClassSetResolverByConstructorInfoTest(OtherClassSetResolverByConstructorInfoTest something) { }

            public class OtherClassSetResolverByConstructorInfoTest { }
        }


        [Test]
        public void ResolveByTypeObjectTest()
        {
            // resolve at least twice to execute both code paths
			var myObject = injector.Resolve(typeof(TestClassTestResolveByTypeObject));
			myObject = injector.Resolve(typeof(TestClassTestResolveByTypeObject));
			myObject = injector.Resolve(typeof(TestClassTestResolveByTypeObject));
        }

        class TestClassTestResolveByTypeObject { }

        [Test]
        public void AddPropertyInjectorTest()
        {
			var binding = injector.Bind<TestClassAddPropertyInjectorTest> ();
			binding.AddPropertyInjector((TestClassAddPropertyInjectorTest v) => v.Other);
			binding.AddPropertyInjector((TestClassAddPropertyInjectorTest v) => v.MyStringProperty, () => "Goldfinger");

			var result = injector.Resolve<TestClassAddPropertyInjectorTest>();

            Assert.AreEqual("Goldfinger", result.MyStringProperty);
            Assert.AreEqual(7, result.Other.Id);
        }

        class TestClassAddPropertyInjectorTest
        {
            public string MyStringProperty { get; set; }
            public OtherClassAddPropertyInjectorTest Other { get; set; }

            public class OtherClassAddPropertyInjectorTest { public int Id { get { return 7; } } }
        }

        [Test]
        public void InterfaceExceptionTest()
        {
			IfFastInjectorException exception = null;
            try
            {
				injector.Resolve<IInterfaceExceptionTest>();
            }
			catch (IfFastInjectorException ex)
            {
                exception = ex;
            }

            Assert.IsNotNull(exception);
        }

        interface IInterfaceExceptionTest { }

        [Test]
        public void AddPropertySetterNotMemberExpression()
        {
			var binding = injector.Bind<TestClassAddPropertyInjectorTest>();

            ArgumentException exception = null;
            try
            {
				binding.AddPropertyInjector((TestClassAddPropertyInjectorTest v) => "");
            }
            catch (ArgumentException ex)
            {
                exception = ex;
            }

            Assert.IsNotNull(exception);
        }

        [Test]
        public void ExceptionConstructorTest()
        {
			var ex1 = new IfFastInjectorException();
			var ex2 = new IfFastInjectorException("something", ex1);

            Assert.IsNotNull(ex1);
            Assert.IsNotNull(ex2);
        }
    }
}
