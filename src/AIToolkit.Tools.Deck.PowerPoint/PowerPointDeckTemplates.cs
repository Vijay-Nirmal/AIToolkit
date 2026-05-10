namespace AIToolkit.Tools.Deck.PowerPoint;

/// <summary>
/// Provides built-in DeckDoc templates for the PowerPoint tool set.
/// </summary>
public static class PowerPointDeckTemplates
{
    /// <summary>
    /// Creates an in-memory template store preloaded with the built-in PowerPoint templates.
    /// </summary>
    public static IDeckTemplateStore CreateDefaultStore()
    {
        var store = new InMemoryDeckTemplateStore();
        store.ImportAsync(CreateDefaultTemplates()).AsTask().GetAwaiter().GetResult();

        return store;
    }

    /// <summary>
    /// Returns the built-in PowerPoint templates.
    /// </summary>
    public static IReadOnlyList<DeckTemplateRecord> CreateDefaultTemplates() =>
    [
        new DeckTemplateRecord(
            "signal-brief",
            "Warm executive briefing with a hero cover, agenda, and narrative update slides.",
            """
            = Signal Brief
            :deckdoc: 1
            :size: wide
            :grid: 32x18

            [theme name=signal-brief]
            [style .title font="Aptos Display" size=30 bold fg=#0F172A]
            [style .body font="Aptos" size=18 fg=#334155 line=1.2]
            [style .accent font="Aptos" size=18 bold fg=#0F766E]
            [style .caption font="Aptos" size=12 fg=#64748B]
            [asset hero "brand://hero"]
            
            [layout cover]
            [grid 32x18]
            [area stage B3:AE15]
            [split stage cols=(13,17) gap=1 as=copy,visual]
            [area footer B17:AE17]
            @title = copy [.title]
            @body = copy [text .body]
            @hero = visual [image fit=cover radius=0.18]
            @footer = footer [.caption]
            [end]
            
            == Cover
            [use cover]
            [transition fade dur=0.35s]
            @title | Quarterly Operations Review
            @body | One focused story about what changed, why it matters, and what happens next.
            @hero [image asset=hero fit=cover alt="Hero image placeholder"]
            @footer | Internal briefing draft
            
            == Agenda
            @title | Agenda
            @body [text .body] | 1. What shifted this quarter
            @body [text .body] | 2. Where we are winning
            @body [text .body] | 3. Decisions and next actions
            
            == Update
            @title | What changed this quarter
            @body [text .body] | Revenue quality improved through better mix, not just higher volume.
            @body [text .body] | Cycle time fell after we simplified approvals and handoffs.
            @body [text .body] | Headcount stayed flat while service levels improved.
            """,
            "builtin"),
        new DeckTemplateRecord(
            "board-update",
            "Clean board-facing deck with headline slides, metric callouts, and decision framing.",
            """
            = Board Update
            :deckdoc: 1
            :size: wide
            :grid: 32x18

            [theme name=board-update]
            [style .title font="Aptos Display" size=32 bold fg=#111827]
            [style .body font="Aptos" size=18 fg=#374151]
            [style .metric font="Aptos Display" size=26 bold fg=#1D4ED8]
            
            == Opening
            [transition fade dur=0.3s]
            @title | FY2026 Board Update
            @body [text .body] | A concise view of performance, outlook, and board decisions.
            
            == Headline Metrics
            @title | Headline Metrics
            @body [text .metric] | ARR +18% year over year
            @body [text .metric] | Gross retention 94%
            @body [text .metric] | Cash runway 28 months
            
            == Decisions
            @title | Decisions Requested
            @body [text .body] | Approve the phased regional expansion.
            @body [text .body] | Maintain product investment in workflow automation.
            @body [text .body] | Hold hiring plan at current operating pace.
            """,
            "builtin"),
        new DeckTemplateRecord(
            "launch-story",
            "Bold launch narrative with problem, proof, and rollout slides.",
            """
            = Launch Story
            :deckdoc: 1
            :size: wide
            :grid: 32x18

            [theme name=launch-story]
            [style .title font="Aptos Display" size=34 bold fg=#0B132B]
            [style .body font="Aptos" size=18 fg=#1F2937]
            [style .kicker font="Aptos" size=16 bold fg=#D97706]
            [asset product-shot "brand://product-shot"]
            
            == Launch Cover
            [transition fade dur=0.4s]
            @title | Introducing Workflow Studio
            @body [text .kicker] | A faster way to build reliable internal automations.
            @visual [image asset=product-shot fit=contain alt="Product mockup placeholder"]
            
            == Problem
            @title | The problem we are solving
            @body [text .body] | Teams lose time stitching together repetitive approvals, updates, and follow-ups.
            @body [text .body] | Existing tools are either too rigid for operators or too fragile for IT.
            
            == Proof
            @title | Why this launch matters
            @body [text .body] | Early design partners cut turnaround time by 42% on recurring requests.
            @body [text .body] | Adoption grew fastest where templates and reusable assets were bundled together.
            
            == Rollout
            @title | Rollout plan
            @body [text .body] | Start with customer support and finance operations.
            @body [text .body] | Add enablement content, reusable templates, and live office hours in week one.
            """,
            "builtin"),
    ];
}
