//-----------------------------------------------------------------------
// <copyright file="IndexDefinition.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Raven.Abstractions.Indexing;
using Raven.Client.Document;

namespace Raven.Client.Indexes
{
	/// <summary>
	/// This class provides a way to define a strongly typed index on the client.
	/// </summary>
	public class IndexDefinitionBuilder<TDocument, TReduceResult> 
	{
		/// <summary>
		/// Gets or sets the map function
		/// </summary>
		/// <value>The map.</value>
		public Expression<Func<IEnumerable<TDocument>, IEnumerable>> Map { get; set; }
		/// <summary>
		/// Gets or sets the reduce function
		/// </summary>
		/// <value>The reduce.</value>
		public Expression<Func<IEnumerable<TReduceResult>, IEnumerable>> Reduce { get; set; }

		/// <summary>
		/// Gets or sets the reduce function
		/// </summary>
		/// <value>The reduce.</value>
		public Expression<Func<IClientSideDatabase,IEnumerable<TReduceResult>, IEnumerable>> TransformResults { get; set; }

	    /// <summary>
		/// Gets or sets the stores options
		/// </summary>
		/// <value>The stores.</value>
		public IDictionary<Expression<Func<TReduceResult, object>>, FieldStorage> Stores { get; set; }
		/// <summary>
		/// Gets or sets the indexing options
		/// </summary>
		/// <value>The indexes.</value>
		public IDictionary<Expression<Func<TReduceResult, object>>, FieldIndexing> Indexes { get; set; }
		/// <summary>
		/// Gets or sets the sort options.
		/// </summary>
		/// <value>The sort options.</value>
		public IDictionary<Expression<Func<TReduceResult, object>>, SortOptions> SortOptions { get; set; }
		/// <summary>
		/// Get os set the analyzers
		/// </summary>
		public IDictionary<Expression<Func<TReduceResult, object>>, string> Analyzers { get; set; }

	    /// <summary>
		/// Initializes a new instance of the <see cref="IndexDefinitionBuilder{TDocument,TReduceResult}"/> class.
		/// </summary>
		public IndexDefinitionBuilder()
		{
			Stores = new Dictionary<Expression<Func<TReduceResult, object>>, FieldStorage>();
			Indexes = new Dictionary<Expression<Func<TReduceResult, object>>, FieldIndexing>();
			SortOptions = new Dictionary<Expression<Func<TReduceResult, object>>, SortOptions>();
			Analyzers = new Dictionary<Expression<Func<TReduceResult, object>>, string>();
		}

		/// <summary>
		/// Toes the index definition.
		/// </summary>
		/// <param name="convention">The convention.</param>
		/// <returns></returns>
		public IndexDefinition ToIndexDefinition(DocumentConvention convention)
		{
			if (Map == null)
				throw new InvalidOperationException(
					string.Format("Map is required to generate an index, you cannot create an index without a valid Map property (in index {0}).", GetType().Name));

			//if (Reduce != null)
			//{
			//    ValidateExpression(Map, "Map");
			//    ValidateExpression(Reduce, "Reduce");
			//}

		    string querySource = (typeof(TDocument) == typeof(object) || ContainsWhereEntityIs(Map.Body)) ? "docs" : "docs." + convention.GetTypeTagName(typeof(TDocument));
		    return new IndexDefinition
			{
				Map = IndexDefinitionHelper.PruneToFailureLinqQueryAsStringToWorkableCode<TDocument>(Map, convention, querySource, translateIdentityProperty:true),
				Reduce = IndexDefinitionHelper.PruneToFailureLinqQueryAsStringToWorkableCode<TDocument>(Reduce, convention, "results", translateIdentityProperty: false),
				TransformResults = IndexDefinitionHelper.PruneToFailureLinqQueryAsStringToWorkableCode<TDocument>(TransformResults, convention, "results", translateIdentityProperty: false),
				Indexes = ConvertToStringDictionary(Indexes),
				Stores = ConvertToStringDictionary(Stores),
				SortOptions = ConvertToStringDictionary(SortOptions),
				Analyzers = ConvertToStringDictionary(Analyzers)
			};
		}

		private void ValidateExpression(LambdaExpression expression, string name)
		{
			if (expression.Body.NodeType != ExpressionType.Call)
				throw new InvalidOperationException(name +" should end with a call to Select()");

			var methodName = ((MethodCallExpression) (expression.Body)).Method.Name;
			if (methodName != "Select" && methodName != "SelectMany")
				throw new InvalidOperationException(name +" should end with a call to Select()");

			var returnType = ((MethodCallExpression)(expression.Body)).Method.ReturnType;
			if (returnType.IsGenericType == false)
				throw new InvalidOperationException(name + " should end with a call to Select(), which should return a generic type");
			var genericArguments = returnType.GetGenericArguments();
			if (genericArguments.Length != 1)
				throw new InvalidOperationException(name + " should end with a call to Select(), which should return a generic type with a single argument");
			var anonymousFields = 
				genericArguments[0]
					.GetProperties().Select(x => x.Name)
					.Union(
						genericArguments[0].GetFields().Select(x=>x.Name)
					)
				.ToArray();

			var reduceResultType = typeof (TReduceResult);
			foreach (var field in anonymousFields.Where(field => reduceResultType.GetProperty(field) == null && reduceResultType.GetField(field) == null))
			{
				throw new InvalidOperationException(
					string.Format("could  not generate an index, the return type {0} does not contain a member with the name {1}",
					              reduceResultType.FullName, field));
			}
		}

#if !NET_3_5
	    private static bool ContainsWhereEntityIs(Expression body)
	    {
	        var whereEntityIsVisitor = new WhereEntityIsVisitor();
	        whereEntityIsVisitor.Visit(body);
	        return whereEntityIsVisitor.HasWhereEntityIs;
	    }

	    private class WhereEntityIsVisitor : ExpressionVisitor
		{
			public bool HasWhereEntityIs { get; set; }

			protected override Expression VisitMethodCall(MethodCallExpression node)
			{
				if (node.Method.Name == "WhereEntityIs")
					HasWhereEntityIs = true;
				return base.VisitMethodCall(node);
			}
		}
#else
		 private static bool ContainsWhereEntityIs(Expression body)
	    {
	        return body.ToString().Contains("WhereEntityIs");
	    }
#endif

		private static IDictionary<string, TValue> ConvertToStringDictionary<TValue>(IEnumerable<KeyValuePair<Expression<Func<TReduceResult, object>>, TValue>> input)
		{
			var result = new Dictionary<string, TValue>();
			foreach (var value in input)
			{
				result[(GetMemberExpression(value.Key)).Member.Name] = value.Value;
			}
			return result;
		}

		private static MemberExpression GetMemberExpression(Expression<Func<TReduceResult, object>> value)
		{
			if(value.Body is UnaryExpression)
				return (MemberExpression)((UnaryExpression) value.Body).Operand;
			return (MemberExpression) value.Body;
		}

	}

	/// <summary>
	/// This class provides a way to define a strongly typed index on the client.
	/// </summary>
	public class IndexDefinitionBuilder<TDocument> : IndexDefinitionBuilder<TDocument, TDocument> { }
}
