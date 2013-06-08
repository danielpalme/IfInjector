﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

/// <summary>
/// NOTE: no actual implementation
/// I was was coding these interfaces before rebasing on top of 'fFastInjector' to use as a starting point.
/// 
/// I intend to merge the good parts of this with IFFastInjector
/// </summary>
namespace IfFastInjector
{
	/// <summary>
	/// Static encapsulation class to restict external access of components.
	/// </summary>
	internal static class IfFastInjectorInternal 
	{

		/// <summary>
		/// The actual injector implementation.
		/// </summary>
		internal class InjectorInternal : Injector
		{
			/// <summary>
			/// Thread safe dictionary wrapper. 
			/// </summary>
			protected internal class SafeDictionary<TKey,TValue> {
				private readonly object syncLock = new object();
				private readonly Dictionary<TKey, TValue> dict = new Dictionary<TKey, TValue>();

				public TValue this [TKey key] {
					set {
						lock (syncLock) {
							dict [key] = value;
						}
					}
				}

				public bool TryGetValueOnly(TKey key, out TValue value) {
					lock (syncLock) {
						return dict.TryGetValue(key, out value);
					}
				}

				public TValue GetWithInitial(TKey key, Func<TValue> initializer) {
					lock (syncLock) {
						TValue value;
						if (!dict.TryGetValue (key, out value)) {
							dict [key] = value = initializer.Invoke ();
						}

						return value;
					}
				}
			}

			protected internal interface IInternalResolver {
				Func<object> GetResolver();
			}

			private class LoopCheckerConstType : IInternalResolver { 
				public Func<object> GetResolver() { return null; }
			}

			private static IInternalResolver LoopCheckerConst = new LoopCheckerConstType();

			// Thread safety via lock (internalResolvers) 
			private readonly Dictionary<Type, IInternalResolver> internalResolvers = new Dictionary<Type, IInternalResolver>();
			protected internal readonly SafeDictionary<Type, bool> isRecursionTestPending = new SafeDictionary<Type, bool> ();
			protected internal readonly MethodInfo GenericResolve;

			/// <summary>
			/// Initializes a new instance of the <see cref="fFastInjector.Injector"/> class.
			/// </summary>
			public InjectorInternal()
			{
				Expression<Func<Exception>> TmpResolveExpression = () => this.Resolve<Exception>();
				this.GenericResolve = ((MethodCallExpression)TmpResolveExpression.Body).Method.GetGenericMethodDefinition();
				this.ExceptionType = typeof(IfFastInjectorException);
			}

			public override T Resolve<T>()
			{
				return GetInternalResolver<T>().Resolve();
			}

			public override object Resolve(Type type)
			{
				IInternalResolver internalResolver;

				if (internalResolvers.TryGetValue(type, out internalResolver))
				{
					return (internalResolver.GetResolver())();
				}

				// Not in dictionary, call Resolve<T> which will, in turn, set up and call the default Resolver
				return GenericResolve.MakeGenericMethod(type).Invoke(this, new object[0]);
			}

			public override IfFastInjectorFluent<T> SetResolver<T, TConcreteType>()
			{
				GetInternalResolver<T>().SetResolver(() => GetInternalResolver<TConcreteType>().Resolve());
				return new InjectorFluent<T>(this);
			}

			public override IfFastInjectorFluent<T> SetResolver<T>(ConstructorInfo constructor)
			{
				GetInternalResolver<T>().SetResolver(constructor);
				return new InjectorFluent<T>(this);
			}

			public override IfFastInjectorFluent<T> SetResolver<T> (Expression<Func<T>> factoryExpression)
			{
				GetInternalResolver<T>().SetResolver(factoryExpression);
				return new InjectorFluent<T>(this);
			}

			public override IfFastInjectorFluent<T> AddPropertyInjector<T, TPropertyType>(Expression<Func<T, TPropertyType>> propertyExpression)
			{
				GetInternalResolver<T>().AddPropertySetter(propertyExpression);
				return new InjectorFluent<T>(this);
			}

			public override IfFastInjectorFluent<T> AddPropertyInjector<T, TPropertyType>(Expression<Func<T, TPropertyType>> propertyExpression, Expression<Func<TPropertyType>> setter)
			{
				GetInternalResolver<T>().AddPropertySetter(propertyExpression, setter);
				return new InjectorFluent<T>(this);
			}

			/// <summary>
			/// The fluent class is really only important to give the extension methods the type for T
			/// </summary>
			/// <typeparam name="T"></typeparam>
			private class InjectorFluent<T> : IfFastInjectorFluent<T>
				where T : class 
			{ 
				private readonly InjectorInternal injector;

				protected internal InjectorFluent(InjectorInternal injector) {
					this.injector = injector;
				}

				public IfFastInjectorFluent<T> AddPropertyInjector<TPropertyType>(Expression<Func<T, TPropertyType>> propertyExpression)
					where TPropertyType : class
				{
					injector.GetInternalResolver<T>().AddPropertySetter(propertyExpression);
					return this;
				}

				public IfFastInjectorFluent<T> AddPropertyInjector<TPropertyType> (Expression<Func<T, TPropertyType>> propertyExpression, Expression<Func<TPropertyType>> setter)
					where TPropertyType : class 
				{
					injector.GetInternalResolver<T>().AddPropertySetter(propertyExpression, setter);
					return this;
				}
			}

			protected internal InternalResolver<T> GetInternalResolver<T>() where T : class {
				return (InternalResolver<T>) GetInternalResolver (typeof(T));
			}

			// TODO - review
			protected internal object GetInternalResolver(Type type) {
				lock (internalResolvers) {
					IInternalResolver resolver;
					if (internalResolvers.TryGetValue (type, out resolver)) {
						if (Object.ReferenceEquals(LoopCheckerConst, resolver)) {
							throw CreateException(string.Format(IfFastInjectorErrors.ErrorResolutionRecursionDetected, type.Name));
						}
						return resolver;
					} else {
						Type iResolverType = typeof(InternalResolver<>);
						Type genericType = iResolverType.MakeGenericType(new Type[] { type });

						internalResolvers[type] = LoopCheckerConst; // TODO I belive this can be eliminated somehow..
						internalResolvers[type] = resolver = CreateInstance (genericType, this);
						return resolver;
					}
				}
			}

			/// <summary>
			/// Creates the instance helper. This method will unpack TargetInvocationException exceptions.
			/// </summary>
			/// <returns>The instance.</returns>
			/// <param name="type">Type.</param>
			/// <param name="args">Arguments.</param>
			private IInternalResolver CreateInstance(Type type, params object[] args) {
				try {
					return (IInternalResolver) Activator.CreateInstance(type, args);
				} catch (TargetInvocationException ex) {
					throw ex.InnerException;
				}
			}

			protected internal Exception CreateException(string message, Exception innerException = null)
			{
				return (Exception) Activator.CreateInstance(ExceptionType, message, innerException);
			}

			protected internal class InternalResolver<T> : IInternalResolver
				where T : class
			{
				protected internal InjectorInternal MyInjector;

				private bool isVerifiedNotRecursive;

				private readonly Type typeofT = typeof(T);
				private readonly List<SetterExpression> setterExpressions = new List<SetterExpression>();
				private Func<T> resolverFactoryCompiled;

				protected internal Func<T> Resolve;

				public InternalResolver(InjectorInternal injector) {
					this.MyInjector = injector;
					this.Resolve = InitInitialResolver();
				}

				public Func<object> GetResolver() {
					return () => {
						return Resolve();
					};
				}

				private Func<T> InitInitialResolver()
				{
					if (typeofT.IsInterface || typeofT.IsAbstract)
					{
						// if we can not instantiate, set the resolver to throw an exception.
						// this resolver will be replaced when the type is configured
						return SetResolverInner(() => ThrowInterfaceException());
					}
					else
					{
						// try to find the default constructor and create a default resolver from it
						return SetDefaultConstructor();
					}
				}

				/// <summary>
				/// Return an InvocationExpression for Resolver of type T
				/// </summary>
				/// <returns></returns>
				private InvocationExpression GetResolverInvocationExpression()
				{
					Expression<Func<T>> expressionForResolverLambda = () => Resolve();
					return (InvocationExpression)expressionForResolverLambda.Body;
				}

				/// <summary>
				/// Return an InvocationExpression for Resolver of type parameterType
				/// </summary>
				/// <param name="parameterType"></param>
				/// <returns></returns>
				private InvocationExpression GetResolverInvocationExpressionForType(Type parameterType)
				{
					var iResolver = MyInjector.GetInternalResolver (parameterType);

					Expression<Func<InvocationExpression>> method = () => GetResolverInvocationExpression();
					var methodName = ((MethodCallExpression)method.Body).Method.Name;
					var methodInst = iResolver.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);

					return (InvocationExpression) methodInst.Invoke(iResolver, new object[0]);
				}

				private T ThrowInterfaceException()
				{
					throw MyInjector.CreateException(string.Format(IfFastInjectorErrors.ErrorUnableToResultInterface, typeof(T).FullName));
				}

				private class SetterExpression
				{
					public MemberExpression PropertyMemberExpression { get; set; }
					public LambdaExpression Setter { get; set; }
				}

				/// <summary>
				/// Expression to construct new instance of class
				/// </summary>
				public Expression<Func<T>> ResolverFactoryExpression { get; private set; }

				/// <summary>
				/// Expression to construct new instance of class and set members or other operations
				/// </summary>
				public Expression<Func<T>> ResolverExpression { get; private set; }

				public void SetResolver(Expression<Func<T>> factoryExpression)
				{
					var visitor = new ReplaceMethodCallWithInvocationExpressionVisitor<T>(this);
					var newFactoryExpression = (Expression<Func<T>>)visitor.Visit(factoryExpression);
					SetResolverInner(newFactoryExpression);
				}

				public void SetResolver(ConstructorInfo constructor)
				{
					SetConstructor(constructor);
				}

				private Func<T> SetResolverInner(Expression<Func<T>> factoryExpression)
				{
					ResolverFactoryExpression = factoryExpression;
					return CompileResolver();
				}

				/// <summary>
				/// Add property setter for property, use the Resolver to determine the value of the property
				/// </summary>
				/// <typeparam name="TPropertyType"></typeparam>
				/// <param name="propertyExpression"></param>
				public void AddPropertySetter<TPropertyType>(Expression<Func<T, TPropertyType>> propertyExpression)
					where TPropertyType : class
				{
					Expression<Func<TPropertyType>> setter = () => MyInjector.GetInternalResolver<TPropertyType>().Resolve();
					AddPropertySetterInner<TPropertyType>(propertyExpression, setter);
				}

				/// <summary>
				/// Add property setter for the property, compile and use the expression for the value of the property
				/// </summary>
				/// <typeparam name="TPropertyType"></typeparam>
				/// <param name="propertyExpression"></param>
				/// <param name="setter"></param>
				public void AddPropertySetter<TPropertyType>(Expression<Func<T, TPropertyType>> propertyExpression, Expression<Func<TPropertyType>> setter)
					where TPropertyType : class
				{
					AddPropertySetterInner<TPropertyType>(propertyExpression, setter);
				}

				private void AddPropertySetterInner<TPropertyType>(Expression<Func<T, TPropertyType>> propertyExpression, Expression<Func<TPropertyType>> setter)
				{
					var propertyMemberExpression = propertyExpression.Body as MemberExpression;
					if (propertyMemberExpression == null)
					{
						throw new ArgumentException(IfFastInjectorErrors.ErrorMustContainMemberExpression, "propertyExpression");
					}

					setterExpressions.Add(new SetterExpression { PropertyMemberExpression = propertyMemberExpression, Setter = setter });

					CompileResolver();
				}

				/// <summary>
				/// Compile the resolver expression
				/// If any setter expressions are used, build an expression that creates the object and then sets the properties before returning it,
				/// otherwise, use the simpler expression that just returns the object
				/// </summary>
				private Func<T> CompileResolver()
				{
					// if no property expressions, then just use the unmodified ResolverFactoryExpression
					if (setterExpressions.Any())
					{
						var resolver = ResolverFactoryExpression;

						var variableExpression = Expression.Variable(typeofT);
						var assignExpression = Expression.Assign(variableExpression, resolver.Body);

						// begin list of expressions for block expression
						var blockExpression = new List<Expression>();
						blockExpression.Add(assignExpression);

						// setters
						foreach (var v in setterExpressions)
						{
							var propertyExpression = Expression.Property(variableExpression, (PropertyInfo)v.PropertyMemberExpression.Member);
							var propertyAssignExpression = Expression.Assign(propertyExpression, v.Setter.Body);
							blockExpression.Add(propertyAssignExpression);
						}

						// return value
						blockExpression.Add(variableExpression);

						var expression = Expression.Block(new ParameterExpression[] { variableExpression }, blockExpression);

						ResolverExpression = (Expression<Func<T>>)Expression.Lambda(expression, resolver.Parameters);
					}
					else
					{
						ResolverExpression = ResolverFactoryExpression;
					}

					this.isVerifiedNotRecursive = false;

					resolverFactoryCompiled = ResolverExpression.Compile();
					Resolve = ResolveWithRecursionCheck;

					return ResolveWithRecursionCheck;
				}

				/// <summary>
				/// Convert expression of Func T to expression of Func object
				/// </summary>
				/// <param name="func"></param>
				/// <returns></returns>
				private static Expression<Func<object>> ConvertFunc(Expression<Func<T>> func)
				{
					return (Expression<Func<object>>)Expression.Lambda(Expression.Convert(func.Body, typeof(object)), func.Parameters);
				}

				private bool isRecursionTestPending {
					get {
						return MyInjector.isRecursionTestPending.GetWithInitial (typeofT, () => false);
					}
					set {
						MyInjector.isRecursionTestPending [typeofT] = value;
					}
				}

				private T ResolveWithRecursionCheck()
				{
					if (!isVerifiedNotRecursive)
					{
						if (isRecursionTestPending)
						{
							throw MyInjector.CreateException(string.Format(IfFastInjectorErrors.ErrorResolutionRecursionDetected, typeofT.Name));
						}
						isRecursionTestPending = true;
					}

					var retval = resolverFactoryCompiled();

					isVerifiedNotRecursive = true;
					isRecursionTestPending = false;
					Resolve = resolverFactoryCompiled;
					return retval;
				}

				/// <summary>
				/// Get the constructor with the fewest number of parameters and create a factory for it
				/// </summary>
				private Func<T> SetDefaultConstructor()
				{
					// get first available constructor ordered by parameter count ascending
					var constructor = typeofT.GetConstructors().Where(v => Attribute.IsDefined(v, typeof(IgnoreConstructorAttribute)) == false).OrderBy(v => Attribute.IsDefined(v, typeof(InjectAttribute)) ? 0 : 1).ThenBy(v => v.GetParameters().Count()).FirstOrDefault();

					if (constructor != null)
					{
						return SetConstructor(constructor);
					}

					return null;
				}

				/// <summary>
				/// Create an expression to create this type from the passed-in constructor
				/// </summary>
				/// <param name="constructor"></param>
				private Func<T> SetConstructor(ConstructorInfo constructor)
				{
					var methodParameters = constructor.GetParameters().Select(v => v.ParameterType).ToArray();

					var arguments = new List<Expression>();

					foreach (var parameterType in methodParameters)
					{
						var argument = GetResolverInvocationExpressionForType(parameterType);
						arguments.Add(argument);
					}

					Expression createInstanceExpression = Expression.New(constructor, arguments);

					return SetResolverInner((Expression<Func<T>>)Expression.Lambda(createInstanceExpression));
				}

				private class ReplaceMethodCallWithInvocationExpressionVisitor<RMT> : ExpressionVisitor
					where RMT : class
				{
					private readonly InternalResolver<RMT> resolver;
					private readonly MethodInfo resolverMethod;

					public ReplaceMethodCallWithInvocationExpressionVisitor(InternalResolver<RMT> resolver)
					{
						this.resolver = resolver;
						this.resolverMethod = resolver.MyInjector.GenericResolve;
					}

					protected override Expression VisitMethodCall(MethodCallExpression node)
					{
						var method = node.Method;
						if (method.IsGenericMethod && method.GetGenericMethodDefinition() == resolverMethod)
						{
							var parameterType = method.GetGenericArguments()[0];
							return resolver.GetResolverInvocationExpressionForType(parameterType);
						}
						else
						{
							return base.VisitMethodCall(node);
						}
					}
				}
			}
		}
	}
}