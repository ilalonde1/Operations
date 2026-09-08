using System.Data;
using Kor.Operations.StandardDetails;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.StandardDetails;

/// <summary>
/// Covers actor/basis SQL parameter values, Unicode types, and the migration's declared sizes.
/// Does not connect to SQL, execute migration 079, or prove a GovernanceLog row was written.
/// A same-class fault this would not catch: a write method omitting AddGovernanceParameters entirely.
/// </summary>
public sealed class PromoterGovernanceParameterTests
{
    [Fact]
    public void Actor_and_basis_are_sent_as_sized_unicode_parameters()
    {
        using var command = new SqlCommand();

        KorStandardsPromoterRepository.AddGovernanceParameters(command, "ilalonde@korstructural.com",
            "Type set in Operations Standard Details");

        var actor = command.Parameters["@ChangedBy"];
        Assert.Equal("ilalonde@korstructural.com", actor.Value);
        Assert.Equal(SqlDbType.NVarChar, actor.SqlDbType);
        Assert.Equal(150, actor.Size);
        var basis = command.Parameters["@Basis"];
        Assert.Equal("Type set in Operations Standard Details", basis.Value);
        Assert.Equal(SqlDbType.NVarChar, basis.SqlDbType);
        Assert.Equal(1000, basis.Size);
    }
}
