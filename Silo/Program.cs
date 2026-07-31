using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("orleans-db")
    ?? throw new InvalidOperationException("Connection string 'orleans-db' not found");

await BootstrapDatabaseAsync(connectionString);

builder.UseOrleans(silo =>
{
    silo.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = connectionString;
    });

    silo.AddAdoNetGrainStorage("AdoNet", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = connectionString;
    });
});

using IHost host = builder.Build();

await host.RunAsync();

static async Task BootstrapDatabaseAsync(string connectionString)
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    await using var check = conn.CreateCommand();
    check.CommandText = "SELECT EXISTS (SELECT FROM pg_tables WHERE tablename = 'orleansquery')";
    var exists = (bool)(await check.ExecuteScalarAsync() ?? false);

    if (exists) return;

    const string main = """
        CREATE TABLE OrleansQuery
        (
            QueryKey varchar(64) NOT NULL,
            QueryText varchar(8000) NOT NULL,
            CONSTRAINT OrleansQuery_Key PRIMARY KEY(QueryKey)
        );
        """;

    const string clustering = """
        CREATE TABLE OrleansMembershipVersionTable
        (
            DeploymentId varchar(150) NOT NULL,
            Timestamp timestamptz(3) NOT NULL DEFAULT now(),
            Version integer NOT NULL DEFAULT 0,
            CONSTRAINT PK_OrleansMembershipVersionTable_DeploymentId PRIMARY KEY(DeploymentId)
        );

        CREATE TABLE OrleansMembershipTable
        (
            DeploymentId varchar(150) NOT NULL,
            Address varchar(45) NOT NULL,
            Port integer NOT NULL,
            Generation integer NOT NULL,
            SiloName varchar(150) NOT NULL,
            HostName varchar(150) NOT NULL,
            Status integer NOT NULL,
            ProxyPort integer NULL,
            SuspectTimes varchar(8000) NULL,
            StartTime timestamptz(3) NOT NULL,
            IAmAliveTime timestamptz(3) NOT NULL,
            CONSTRAINT PK_MembershipTable_DeploymentId PRIMARY KEY(DeploymentId, Address, Port, Generation),
            CONSTRAINT FK_MembershipTable_MembershipVersionTable_DeploymentId FOREIGN KEY (DeploymentId) REFERENCES OrleansMembershipVersionTable (DeploymentId)
        );

        CREATE OR REPLACE FUNCTION update_i_am_alive_time(
            deployment_id OrleansMembershipTable.DeploymentId%TYPE,
            address_arg OrleansMembershipTable.Address%TYPE,
            port_arg OrleansMembershipTable.Port%TYPE,
            generation_arg OrleansMembershipTable.Generation%TYPE,
            i_am_alive_time OrleansMembershipTable.IAmAliveTime%TYPE)
          RETURNS void LANGUAGE plpgsql AS $$
        BEGIN
            UPDATE OrleansMembershipTable as d
            SET IAmAliveTime = i_am_alive_time
            WHERE d.DeploymentId = deployment_id
              AND d.Address = address_arg
              AND d.Port = port_arg
              AND d.Generation = generation_arg;
        END;
        $$;

        INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
        ('UpdateIAmAlivetimeKey', 'SELECT * from update_i_am_alive_time(@DeploymentId, @Address, @Port, @Generation, @IAmAliveTime);');

        CREATE OR REPLACE FUNCTION insert_membership_version(DeploymentIdArg OrleansMembershipTable.DeploymentId%TYPE)
          RETURNS TABLE(row_count integer) LANGUAGE plpgsql AS $$
        DECLARE RowCountVar int := 0;
        BEGIN
            BEGIN
                INSERT INTO OrleansMembershipVersionTable (DeploymentId) SELECT DeploymentIdArg ON CONFLICT (DeploymentId) DO NOTHING;
                GET DIAGNOSTICS RowCountVar = ROW_COUNT;
                ASSERT RowCountVar <> 0, 'no rows affected, rollback';
                RETURN QUERY SELECT RowCountVar;
            EXCEPTION WHEN assert_failure THEN
                RETURN QUERY SELECT RowCountVar;
            END;
        END;
        $$;

        INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
        ('InsertMembershipVersionKey', 'SELECT * FROM insert_membership_version(@DeploymentId);');

        CREATE OR REPLACE FUNCTION insert_membership(
            DeploymentIdArg OrleansMembershipTable.DeploymentId%TYPE,
            AddressArg OrleansMembershipTable.Address%TYPE,
            PortArg OrleansMembershipTable.Port%TYPE,
            GenerationArg OrleansMembershipTable.Generation%TYPE,
            SiloNameArg OrleansMembershipTable.SiloName%TYPE,
            HostNameArg OrleansMembershipTable.HostName%TYPE,
            StatusArg OrleansMembershipTable.Status%TYPE,
            ProxyPortArg OrleansMembershipTable.ProxyPort%TYPE,
            StartTimeArg OrleansMembershipTable.StartTime%TYPE,
            IAmAliveTimeArg OrleansMembershipTable.IAmAliveTime%TYPE,
            VersionArg OrleansMembershipVersionTable.Version%TYPE)
          RETURNS TABLE(row_count integer) LANGUAGE plpgsql AS $$
        DECLARE RowCountVar int := 0;
        BEGIN
            BEGIN
                INSERT INTO OrleansMembershipTable (DeploymentId, Address, Port, Generation, SiloName, HostName, Status, ProxyPort, StartTime, IAmAliveTime)
                SELECT DeploymentIdArg, AddressArg, PortArg, GenerationArg, SiloNameArg, HostNameArg, StatusArg, ProxyPortArg, StartTimeArg, IAmAliveTimeArg
                ON CONFLICT (DeploymentId, Address, Port, Generation) DO NOTHING;
                GET DIAGNOSTICS RowCountVar = ROW_COUNT;
                UPDATE OrleansMembershipVersionTable SET Timestamp = now(), Version = Version + 1
                WHERE DeploymentId = DeploymentIdArg AND Version = VersionArg AND RowCountVar > 0;
                GET DIAGNOSTICS RowCountVar = ROW_COUNT;
                ASSERT RowCountVar <> 0, 'no rows affected, rollback';
                RETURN QUERY SELECT RowCountVar;
            EXCEPTION WHEN assert_failure THEN
                RETURN QUERY SELECT RowCountVar;
            END;
        END;
        $$;

        INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
        ('InsertMembershipKey', 'SELECT * FROM insert_membership(@DeploymentId, @Address, @Port, @Generation, @SiloName, @HostName, @Status, @ProxyPort, @StartTime, @IAmAliveTime, @Version);');

        CREATE OR REPLACE FUNCTION update_membership(
            DeploymentIdArg OrleansMembershipTable.DeploymentId%TYPE,
            AddressArg OrleansMembershipTable.Address%TYPE,
            PortArg OrleansMembershipTable.Port%TYPE,
            GenerationArg OrleansMembershipTable.Generation%TYPE,
            StatusArg OrleansMembershipTable.Status%TYPE,
            SuspectTimesArg OrleansMembershipTable.SuspectTimes%TYPE,
            IAmAliveTimeArg OrleansMembershipTable.IAmAliveTime%TYPE,
            VersionArg OrleansMembershipVersionTable.Version%TYPE)
          RETURNS TABLE(row_count integer) LANGUAGE plpgsql AS $$
        DECLARE RowCountVar int := 0;
        BEGIN
            BEGIN
                UPDATE OrleansMembershipVersionTable SET Timestamp = now(), Version = Version + 1
                WHERE DeploymentId = DeploymentIdArg AND Version = VersionArg;
                GET DIAGNOSTICS RowCountVar = ROW_COUNT;
                UPDATE OrleansMembershipTable SET Status = StatusArg, SuspectTimes = SuspectTimesArg, IAmAliveTime = IAmAliveTimeArg
                WHERE DeploymentId = DeploymentIdArg AND Address = AddressArg AND Port = PortArg AND Generation = GenerationArg AND RowCountVar > 0;
                GET DIAGNOSTICS RowCountVar = ROW_COUNT;
                ASSERT RowCountVar <> 0, 'no rows affected, rollback';
                RETURN QUERY SELECT RowCountVar;
            EXCEPTION WHEN assert_failure THEN
                RETURN QUERY SELECT RowCountVar;
            END;
        END;
        $$;

        INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
        ('UpdateMembershipKey', 'SELECT * FROM update_membership(@DeploymentId, @Address, @Port, @Generation, @Status, @SuspectTimes, @IAmAliveTime, @Version);');

        INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
        ('MembershipReadRowKey', 'SELECT v.DeploymentId, m.Address, m.Port, m.Generation, m.SiloName, m.HostName, m.Status, m.ProxyPort, m.SuspectTimes, m.StartTime, m.IAmAliveTime, v.Version FROM OrleansMembershipVersionTable v LEFT OUTER JOIN OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId AND Address = @Address AND Port = @Port AND Generation = @Generation WHERE v.DeploymentId = @DeploymentId;');

        INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
        ('MembershipReadAllKey', 'SELECT v.DeploymentId, m.Address, m.Port, m.Generation, m.SiloName, m.HostName, m.Status, m.ProxyPort, m.SuspectTimes, m.StartTime, m.IAmAliveTime, v.Version FROM OrleansMembershipVersionTable v LEFT OUTER JOIN OrleansMembershipTable m ON v.DeploymentId = m.DeploymentId WHERE v.DeploymentId = @DeploymentId;');

        INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
        ('DeleteMembershipTableEntriesKey', 'DELETE FROM OrleansMembershipTable WHERE DeploymentId = @DeploymentId; DELETE FROM OrleansMembershipVersionTable WHERE DeploymentId = @DeploymentId;');

        INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
        ('GatewaysQueryKey', 'SELECT Address, ProxyPort, Generation FROM OrleansMembershipTable WHERE DeploymentId = @DeploymentId AND Status = @Status AND ProxyPort > 0;');

        INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES
        ('CleanupDefunctSiloEntriesKey', 'DELETE FROM OrleansMembershipTable WHERE DeploymentId = @DeploymentId AND Status = @Status AND IAmAliveTime < @IAmAliveTime;');
        """;

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = main + clustering;
    await cmd.ExecuteNonQueryAsync();

    var hasStorage = (bool)(await check.ExecuteScalarAsync() ?? false);
    check.CommandText = "SELECT EXISTS (SELECT FROM pg_tables WHERE tablename = 'orleansstorage')";
    if (!(bool)(await check.ExecuteScalarAsync() ?? false))
    {
        const string persistence = """
            CREATE TABLE OrleansStorage
            (
                grainidhash integer NOT NULL,
                grainidn0 bigint NOT NULL,
                grainidn1 bigint NOT NULL,
                graintypehash integer NOT NULL,
                graintypestring character varying(512) NOT NULL,
                grainidextensionstring character varying(512),
                serviceid character varying(150) NOT NULL,
                payloadbinary bytea,
                modifiedon timestamp without time zone NOT NULL,
                version integer
            );
            CREATE INDEX ix_orleansstorage ON orleansstorage USING btree (grainidhash, graintypehash);

            CREATE OR REPLACE FUNCTION writetostorage(
                _grainidhash integer, _grainidn0 bigint, _grainidn1 bigint,
                _graintypehash integer, _graintypestring character varying,
                _grainidextensionstring character varying, _serviceid character varying,
                _grainstateversion integer, _payloadbinary bytea)
                RETURNS TABLE(newgrainstateversion integer) LANGUAGE plpgsql AS $function$
            DECLARE
                _newGrainStateVersion integer := _GrainStateVersion;
                RowCountVar integer := 0;
            BEGIN
                IF _GrainStateVersion IS NOT NULL THEN
                    UPDATE OrleansStorage SET PayloadBinary = _PayloadBinary,
                        ModifiedOn = (now() at time zone 'utc'), Version = Version + 1
                    WHERE GrainIdHash = _GrainIdHash AND GrainTypeHash = _GrainTypeHash
                        AND GrainIdN0 = _GrainIdN0 AND GrainIdN1 = _GrainIdN1
                        AND GrainTypeString = _GrainTypeString AND ServiceId = _ServiceId
                        AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
                        AND Version IS NOT NULL AND Version = _GrainStateVersion;
                    GET DIAGNOSTICS RowCountVar = ROW_COUNT;
                    IF RowCountVar > 0 THEN _newGrainStateVersion := _GrainStateVersion + 1; END IF;
                END IF;
                IF _GrainStateVersion IS NULL THEN
                    INSERT INTO OrleansStorage (GrainIdHash, GrainIdN0, GrainIdN1, GrainTypeHash, GrainTypeString, GrainIdExtensionString, ServiceId, PayloadBinary, ModifiedOn, Version)
                    SELECT _GrainIdHash, _GrainIdN0, _GrainIdN1, _GrainTypeHash, _GrainTypeString, _GrainIdExtensionString, _ServiceId, _PayloadBinary, (now() at time zone 'utc'), 1
                    WHERE NOT EXISTS (SELECT 1 FROM OrleansStorage
                        WHERE GrainIdHash = _GrainIdHash AND GrainTypeHash = _GrainTypeHash AND GrainIdN0 = _GrainIdN0 AND GrainIdN1 = _GrainIdN1
                        AND GrainTypeString = _GrainTypeString AND ServiceId = _ServiceId
                        AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL));
                    GET DIAGNOSTICS RowCountVar = ROW_COUNT;
                    IF RowCountVar > 0 THEN _newGrainStateVersion := 1; END IF;
                END IF;
                RETURN QUERY SELECT _newGrainStateVersion AS NewGrainStateVersion;
            END $function$;

            INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES ('WriteToStorageKey','select * from WriteToStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion, @PayloadBinary);');
            INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES ('ReadFromStorageKey','SELECT PayloadBinary, (now() at time zone ''utc''), Version FROM OrleansStorage WHERE GrainIdHash = @GrainIdHash AND GrainTypeHash = @GrainTypeHash AND GrainIdN0 = @GrainIdN0 AND GrainIdN1 = @GrainIdN1 AND GrainTypeString = @GrainTypeString AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL) AND ServiceId = @ServiceId');
            INSERT INTO OrleansQuery(QueryKey, QueryText) VALUES ('ClearStorageKey','UPDATE OrleansStorage SET PayloadBinary = NULL, Version = Version + 1 WHERE GrainIdHash = @GrainIdHash AND GrainTypeHash = @GrainTypeHash AND GrainIdN0 = @GrainIdN0 AND GrainIdN1 = @GrainIdN1 AND GrainTypeString = @GrainTypeString AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL) AND ServiceId = @ServiceId AND Version IS NOT NULL AND Version = @GrainStateVersion Returning Version as NewGrainStateVersion');
            """;

        await using var pcmd = conn.CreateCommand();
        pcmd.CommandText = persistence;
        await pcmd.ExecuteNonQueryAsync();
    }
}
