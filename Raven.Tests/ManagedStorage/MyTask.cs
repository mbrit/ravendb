//-----------------------------------------------------------------------
// <copyright file="MyTask.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using Raven.Database.Indexing;
using Raven.Database.Tasks;

namespace Raven.Tests.ManagedStorage
{
	public class MyTask : Task
	{
		public override bool TryMerge(Task task)
		{
			return true;
		}

		public override void Execute(WorkContext context)
		{
			
		}

		public override Task Clone()
		{
			return new MyTask
			{
				Index = Index,
			};
		}
	}
}