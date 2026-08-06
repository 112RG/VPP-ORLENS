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
