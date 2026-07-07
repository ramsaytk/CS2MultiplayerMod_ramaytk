namespace CS2MultiplayerMod.Core.Protocol
{
    public static class ProtocolConstants
    {
        /// <summary>
        /// Wire-format version. Bump whenever a message's layout changes so that
        /// mismatched host/client builds refuse the handshake instead of
        /// misinterpreting bytes. See <see cref="Messages.HandshakeRequest"/>.
        /// v5: challenge-response auth, mod/game version checks, per-type size caps.
        /// v6: DLC preconditions — the handshake carries the client's owned-DLC list
        /// so host and client refuse to pair when their content differs (idea ported
        /// from the CS2M project's preconditions check).
        /// v7: PlayerState carries the camera eye position as well as the ground focus,
        /// so player markers show height ("flying"), not just a ground point.
        /// v8: NetDeleteCommand carries the segment's full Bézier (b, c handles) instead of
        /// just its endpoints, so a road the two machines subdivided differently still deletes
        /// completely (the receiver matches by curve, not endpoint coincidence).
        /// v9: adds NetReplaceCommand (id 19) — in-place road-type replacement (a different net
        /// prefab drawn over an existing edge), which the game commits as a PrefabRef change with
        /// no Created/Deleted, so no earlier command covered it. The same command (unchanged
        /// layout) also carries in-place direction flips: the Bézier's a→d order is the committed
        /// direction, and the receiver inverts local edges that run the other way.
        /// v10: Heartbeat carries an echo field so latency is measured as a true round-trip on
        /// the sender's own clock; the previous one-way math subtracted two machines' unrelated
        /// monotonic clocks and never produced a usable number.
        /// v11: NetReplaceCommand carries the segment's BASELINE (pre-replacement) Bézier alongside
        /// the committed one. A width-changing replacement commits the edge with a laterally shifted
        /// centerline (half the width difference), so the committed curve alone sat exactly at the
        /// receiver's match tolerance — matching became a coin flip and the two cities' geometry
        /// drifted apart, breaking every later replace/delete of the street. The receiver now finds
        /// its edges on the old curve and re-commits them on the new one.
        /// v12: NetUpgradeCommand covers the whole street-tools family — it gains a node-target
        /// flag (traffic lights, all-way stops and roundabouts live on nodes, which the old
        /// edge-only layout could not address), a sub-replacement list (roadside tree-row styles),
        /// and "all zero" now explicitly means the upgrade was REMOVED (the game strips the
        /// component instead of storing zero flags, so removals previously never shipped).
        /// </summary>
        public const int ProtocolVersion = 12;

        /// <summary>
        /// Hard cap on a single payload, guarding against corrupt length prefixes.
        /// This is the transport-level ceiling; each message type has a far smaller
        /// cap enforced by <see cref="MessageCodec"/>.
        /// </summary>
        public const int MaxPayloadBytes = 16 * 1024 * 1024;

        /// <summary>One blob slice on the wire. Also the per-chunk cap on receive.</summary>
        public const int BlobChunkBytes = 256 * 1024;

        /// <summary>Bytes of nonce in a handshake challenge.</summary>
        public const int ChallengeNonceBytes = 32;

        /// <summary>Bytes of an HMAC-SHA256 password proof.</summary>
        public const int PasswordProofBytes = 32;

        /// <summary>Most DLC entries a handshake may carry (the catalogue is ~2 dozen).</summary>
        public const int MaxDlcEntries = 64;

        /// <summary>Length cap for one DLC name in a handshake.</summary>
        public const int MaxDlcNameLength = 64;
    }
}
