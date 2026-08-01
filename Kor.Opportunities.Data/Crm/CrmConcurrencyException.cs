#nullable enable
using System;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// Raised when an UPDATE on opportunities.CrmEngagements / CrmContacts found
/// no matching row for the supplied (Id, RowVersion) pair — i.e. the row
/// was modified between read and write. Mirrors
/// <see cref="Kor.Opportunities.Data.Opportunities.OpportunityConcurrencyException"/>.
/// </summary>
public sealed class CrmConcurrencyException : Exception
{
    public CrmConcurrencyException(string entity, long id)
        : base($"{entity} {id} was modified by another writer; reload before saving.")
    {
        Entity = entity;
        EntityId = id;
    }

    public string Entity { get; }

    public long EntityId { get; }
}
