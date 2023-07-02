using System.Diagnostics.CodeAnalysis;
using MonoTorrent;
using MonoTorrent.PiecePicking;

namespace MyHomeLib.Files.Core;

public class PartialStreamingRequester : IStreamingPieceRequester
{
    private readonly AppConfig _config;
    private readonly StreamingPieceRequester _pieceRequester = new ();

    public PartialStreamingRequester(AppConfig config)
    {
        _config = config;
    }

    public void AddRequests(ReadOnlySpan<(IRequester Peer, ReadOnlyBitField Available)> peers)
    {
        _pieceRequester.AddRequests(peers);
    }

    public void AddRequests(IRequester peer, ReadOnlyBitField available, ReadOnlySpan<ReadOnlyBitField> peers)
    {
        _pieceRequester.AddRequests(peer, available, peers);
    }

    public bool ValidatePiece(IRequester peer, PieceSegment pieceSegment, [UnscopedRef] out bool pieceComplete,
        HashSet<IRequester> peersInvolved)
    {
        return _pieceRequester.ValidatePiece(peer, pieceSegment, out pieceComplete, peersInvolved);
    }

    public bool IsInteresting(IRequester peer, ReadOnlyBitField bitField)
    {
        var peerName = peer.ToString();
        if (peerName != null && _config.SpecialPeers.Contains(peer.ToString()))
        {
            return true;
        }
        return _pieceRequester.IsInteresting(peer, bitField);
    }

    public void Initialise(IPieceRequesterData torrentData, IMessageEnqueuer enqueuer, ReadOnlySpan<ReadOnlyBitField> ignorableBitfields)
    {
        _pieceRequester.Initialise(torrentData, enqueuer, ignorableBitfields);
    }

    public void CancelRequests(IRequester peer, int startIndex, int endIndex)
    {
        _pieceRequester.CancelRequests(peer, startIndex, endIndex);
    }

    public void RequestRejected(IRequester peer, PieceSegment pieceRequest)
    {
        _pieceRequester.RequestRejected(peer, pieceRequest);
    }

    public int CurrentRequestCount()
    {
        return _pieceRequester.CurrentRequestCount();
    }

    public bool InEndgameMode => _pieceRequester.InEndgameMode;
    
    public void SeekToPosition(ITorrentManagerFile file, long position)
    {
        _pieceRequester.SeekToPosition(file, position);
    }

    public void ReadToPosition(ITorrentManagerFile file, long position)
    {
        _pieceRequester.ReadToPosition(file, position);
    }
}