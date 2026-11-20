using Erp.Setup.Sql.Database;

namespace Erp.Setup.Tests.Sql;

/// <summary>Amprenta din core.ScriptVersion (08, 5.9): o reformatare nu face versiune noua, orice alt caracter da.</summary>
public sealed class ScriptFingerprintTests
{
    private const string Procedure = "CREATE OR ALTER PROCEDURE core.usp_A\nAS\n    SELECT 1;\n";

    [Theory]
    [InlineData("CREATE OR ALTER PROCEDURE core.usp_A\r\nAS\r\n    SELECT 1;\r\n")]
    [InlineData("CREATE OR ALTER PROCEDURE core.usp_A   \nAS\t\n    SELECT 1;\n")]
    [InlineData("CREATE OR ALTER PROCEDURE core.usp_A\nAS\n    SELECT 1;")]
    [InlineData("CREATE OR ALTER PROCEDURE core.usp_A\nAS\n    SELECT 1;\n\n\n")]
    [InlineData("﻿CREATE OR ALTER PROCEDURE core.usp_A\nAS\n    SELECT 1;\n")]
    public void Reformatting_does_not_change_the_fingerprint(string reformatted)
    {
        Assert.Equal(ScriptFingerprint.Compute(Procedure), ScriptFingerprint.Compute(reformatted));
    }

    [Theory]
    [InlineData("CREATE OR ALTER PROCEDURE core.usp_A\nAS\n    SELECT 2;\n")]
    [InlineData("CREATE OR ALTER PROCEDURE core.usp_A\nAS\n    SELECT 1; -- comentariu\n")]
    [InlineData("CREATE OR ALTER PROCEDURE core.usp_A\nAS\n        SELECT 1;\n")]
    [InlineData("create or alter procedure core.usp_A\nAS\n    SELECT 1;\n")]
    public void Any_other_change_does(string changed)
    {
        Assert.NotEqual(ScriptFingerprint.Compute(Procedure), ScriptFingerprint.Compute(changed));
    }

    [Fact]
    public void Fingerprint_is_64_lowercase_hex_characters_as_in_the_table()
    {
        Assert.Matches("^[0-9a-f]{64}$", ScriptFingerprint.Compute(Procedure));
    }
}
