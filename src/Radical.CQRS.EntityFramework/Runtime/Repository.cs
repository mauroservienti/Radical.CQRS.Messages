﻿using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using Newtonsoft.Json;
using Radical.CQRS.Reflection;
using Topics.Radical.Linq;
using System.Threading.Tasks;

namespace Radical.CQRS.Runtime
{
	class Repository : AbstractRepository
	{
		public override void Dispose()
		{
			this._session.Dispose();

			this.AggregateTracking.Clear();
		}

		readonly DbContext _session;

		public Repository( DbContext session )
		{
			this._session = session;
		}

		public override void Add<TAggregate>( TAggregate aggregate )
		{
			var db = this._session.Set<TAggregate>();
			db.Add( aggregate );
			this.TrackIfRequired( aggregate );
		}

		public override async Task CommitChangesAsync()
		{
			try
			{
				var db = this._session.Set<DomainEventCommit>();

				this.AggregateTracking
					.Where( a => a.IsChanged )
					.Select( aggregate => new
					{
						Aggregate = aggregate,
						Commits = aggregate.GetUncommittedEvents().Select( e => new DomainEventCommit()
						{
							EventId = e.Id,
							AggregateId = aggregate.Id,
							TransactionId = this.TransactionId,
							PublishedOn = e.OccurredAt,
							EventType = ConcreteProxyCreator.GetValidTypeName( e.GetType() ),
							EventBlob = JsonConvert.SerializeObject( e ),
							Version = e.AggregateVersion
						} )
					} )
					.SelectMany( a => a.Commits )
					.ToArray()
					.ForEach( temp =>
					{
						db.Add( temp );
					} );

				await this._session.SaveChangesAsync();

				this.AggregateTracking.ForEach( a => a.ClearUncommittedEvents() );
				this.AggregateTracking.Clear();

			}
			catch( Exception )
			{
				//TODO: log
				throw;
			}
		}

		public override async Task<TAggregate> GetByIdAsync<TAggregate>( Guid aggregateId )
		{
			var db = this._session.Set<TAggregate>();
			var aggregate = await db.SingleAsync( a => a.Id == aggregateId );
			this.TrackIfRequired( aggregate );

			return aggregate;
		}

		public override async Task<IEnumerable<TAggregate>> GetByIdAsync<TAggregate>( params Guid[] aggregateIds )
		{
			var db = this._session.Set<TAggregate>();
			var aggregates = await db.Where( a => aggregateIds.Contains( a.Id ) )
				.ToListAsync();
			foreach( var a in aggregates )
			{
				this.TrackIfRequired( a );
			}

			return aggregates;
		}
	}
}
