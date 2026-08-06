CREATE TABLE OrleansQuery
(
    QueryKey varchar(64) NOT NULL,
    QueryText varchar(8000) NOT NULL,
    CONSTRAINT OrleansQuery_Key PRIMARY KEY(QueryKey)
);

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
