namespace PerformanceLog {

  class NetworkNode {
  };

  class NetworkMessage {

    internal static void Read(ProfileLog parser) {
      var count = parser.readVarInt();
      var messageBytes = parser.readVarInt();
      var sentCount = parser.readVarInt();
      var sentBytes = parser.readVarInt();
    }
  }


  class NetworkClass : NetworkNode {

    internal static void Read(ProfileLog parser) {

      var overheadBits = parser.readVarInt();
      var totalBits = parser.readVarInt();
      var baselineBits = parser.readVarInt();
      var diffBits = parser.readVarInt();

      var name = parser.readStringId();

      while (name != "class-end") {
        //NetworkField
        NetworkField.Read(parser);
        //if version > 3:

        name = parser.readStringId();
      }
    }

  }

  class NetworkField : NetworkNode {

    internal static void Read(ProfileLog parser) {
      var baseCount = parser.readVarInt(); // how many bits written;
      var baseBits = parser.readVarInt(); // how many times;
      var diffCount = parser.readVarInt(); // how many bits written;
      var diffBits2 = parser.readVarInt(); // how many times;

      var deltaStatsCount = parser.readVarInt();

      if (deltaStatsCount > 0) {
        NetworkDeltaStats.Read(parser, deltaStatsCount);
      }
    }
  }

  class NetworkDeltaStats : NetworkNode {

    internal static void Read(ProfileLog parser, uint numLines) {

      var longestLine = 0;

      var count = parser.readVarInt();

      for (int i = 0; i < numLines; i++) {
        //lines.append(
        NetworkDeltaStatsLine.Read(parser);
        // longestLine = max(var longestLine, len(var lines[-1].values));
      }

      var numDeltas = parser.readVarInt();

      for (int i = 0; i < numDeltas; i++) {
        NetworkDeltaStatsPerDeltaLine.Read(parser, numLines);
      }
    }
  }

  class NetworkDeltaStatsLine : NetworkNode {

    string className = "<Unknown>";
    uint length;

    internal static void Read(ProfileLog parser) {
      //NetworkNode.__init__(self, "%s-%d" % (parent.name,id), parent)
      //self.values = []
      //id = id;
      //if parser:
      //className = self.parent.parent.parent.getName()
      var length = parser.readVarInt();

      for (int i = 0; i < length; i++) {
        //self.values.append(
        parser.readVarInt();
      }
    }
  }

  class NetworkDeltaStatsPerDeltaLine : NetworkNode {

    internal static void Read(ProfileLog parser, uint numValues) {
      // NetworkNode.__init__(self, "%s-D%d" % (parent.name,id), parent)
      //var bits = new uint[numValues];
      //var missCounts = new uint[numValues];
      //var decLossCounts = new uint[numValues];
      //var incHitCounts = new uint[numValues];
      //var totalCount = var hitCount = var allMissCount = 0

      //if parser:
      var totalCount = parser.readVarInt();
      var hitCount = parser.readVarInt();
      var allMissCount = parser.readVarInt();

      for (int i = 0; i < numValues; i++) {
        /*bits[i] */
        var bits = parser.readVarInt();
        /*missCounts[i] */
        var missCounts = parser.readVarInt();
        /*//if parser.version > 4:
        /*decLossCounts[i] */
        var decLossCounts = parser.readVarInt();
        /*incHitCounts[i] */
        var incHitCounts = parser.readVarInt();
      }
    }

  }

  class NetworkClientInfo {

    internal static void Read(FastBinaryReader parser) {
      var snapBytes = parser.readVarInt();
      var msUsed = parser.readVarInt();
      //if version > 5:
      var bytesSent = parser.readVarInt();
      var voiceBytesSent = parser.readVarInt();
    }
  }
}
