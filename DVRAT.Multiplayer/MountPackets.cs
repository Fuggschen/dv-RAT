using MPAPI.Interfaces.Packets;

namespace DVRAT.Multiplayer
{
    // LiteNetLib writes every public property.

    /// <summary>See <see cref="GadgetMounts.TryDescribe"/>.</summary>
    public class RatMountPacket : IPacket
    {
        /// <summary>The sender's mount and removal count. Reliable packets can arrive out of order, so an older one than applied is dropped.</summary>
        public uint Seq { get; set; }

        public string Id { get; set; }

        /// <summary>The car's Multiplayer net ID, which is the same in every player's game.</summary>
        public ushort Car { get; set; }

        public string Prefab { get; set; }

        /// <summary>The item's save data as JSON.</summary>
        public string State { get; set; }

        /// <summary>A tablet's synced state, see <see cref="TabletSync"/>. Empty for a speaker, as packets carry no null.</summary>
        public string Session { get; set; } = "";
    }

    /// <summary>A joining player's name as the host knows it, since the Multiplayer mod renames duplicate names.</summary>
    public class RatNamePacket : IPacket
    {
        public string Name { get; set; }
    }

    /// <summary>What a tablet shows, see <see cref="TabletSync"/>.</summary>
    public class RatSessionPacket : IPacket
    {
        /// <summary>See <see cref="RatMountPacket.Seq"/>.</summary>
        public uint Seq { get; set; }

        /// <summary>The tablet's ID, see <see cref="TabletSession.Id"/>.</summary>
        public string Id { get; set; }

        public string State { get; set; }
    }

    /// <summary>Opus audio, see <see cref="OpusAudio"/>. Sent unreliably and must stay small, as unreliable packets cannot be split.</summary>
    public class RatAudioPacket : IPacket
    {
        /// <summary>The tablet's ID, see <see cref="TabletSession.Id"/>.</summary>
        public string Id { get; set; }

        public uint Epoch { get; set; }

        public uint Seq { get; set; }

        public byte[] Data { get; set; }
    }

    /// <summary>A key pressed on a tablet copy, for the copy that plays. See <see cref="MediaPlayer.RunCommand"/>.</summary>
    public class RatMediaCommandPacket : IPacket
    {
        /// <summary>The tablet's ID, see <see cref="TabletSession.Id"/>.</summary>
        public string Id { get; set; }

        /// <summary>The player whose copy plays; the host passes the key on to them.</summary>
        public string Source { get; set; }

        public string Command { get; set; }
    }

    /// <summary>A speaker's volume and EQ, see <see cref="SpeakerSync"/>.</summary>
    public class RatSpeakerPacket : IPacket
    {
        /// <summary>See <see cref="RatMountPacket.Seq"/>.</summary>
        public uint Seq { get; set; }

        /// <summary>The speaker's ID, see <see cref="SpeakerSettings.Id"/>.</summary>
        public string Id { get; set; }

        public string State { get; set; }
    }

    /// <summary>The host's fees, see <see cref="SharedFees.Describe"/>.</summary>
    public class RatFeesPacket : IPacket
    {
        public string Json { get; set; }
    }

    /// <summary>The host's loco odometers, see <see cref="Odometer.Describe"/>.</summary>
    public class RatOdometerPacket : IPacket
    {
        public string Json { get; set; }
    }

    public class RatUnmountPacket : IPacket
    {
        /// <summary>See <see cref="RatMountPacket.Seq"/>.</summary>
        public uint Seq { get; set; }

        public string Id { get; set; }
    }
}
