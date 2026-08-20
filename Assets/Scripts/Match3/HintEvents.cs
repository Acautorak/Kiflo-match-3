/// <summary>
/// Published by HintController when PossibleMoveFinder finds no valid move anywhere on the
/// board. Classic match-3 behavior is to reshuffle when this happens - not implemented here since
/// reshuffling touches Board's populate/spawn logic directly; hook OnNoValidMovesFound (or this
/// event) up to a reshuffle routine if you want that.
/// </summary>
public readonly struct NoValidMovesFoundEvent
{
}
