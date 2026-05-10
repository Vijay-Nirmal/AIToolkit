using AIToolkit.Tools.Deck;

namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Exposes the shared DeckDoc parser to the PowerPoint provider.
/// </summary>
internal static class DeckDocParser
{
    /// <summary>
    /// Parses canonical DeckDoc into the shared syntax model.
    /// </summary>
    public static DeckDocDocument Parse(string deckDoc) =>
        DeckDocSyntaxParser.Parse(deckDoc);
}
