using System.Text.Json;

namespace Mem0Sharp.Evaluation;

/// <summary>
/// A self-contained evaluation dataset of fictional multi-session conversations.
/// All people, places, and events are invented for benchmarking only.
/// Question categories follow the LOCOMO benchmark style: single-hop recall,
/// multi-hop reasoning, temporal reasoning, and adversarial (unanswerable) questions.
/// </summary>
internal static class EvaluationDataset
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal const string CategorySingleHop = "single-hop";
    internal const string CategoryMultiHop = "multi-hop";
    internal const string CategoryTemporal = "temporal";
    internal const string CategoryAdversarial = "adversarial";

    internal static readonly string[] Categories =
    [
        CategorySingleHop,
        CategoryMultiHop,
        CategoryTemporal,
        CategoryAdversarial
    ];

    internal static IReadOnlyList<EvalConversation> Load() =>
    [
        new EvalConversation(
            Id: "mara-and-jules",
            SpeakerA: "Mara",
            SpeakerB: "Jules",
            Sessions:
            [
                new EvalSession("2025-01-08",
                [
                    new EvalTurn("Mara", "I finally moved into the second-floor apartment by the bakery. It is quieter than the old place and I can work without hearing the street noise."),
                    new EvalTurn("Jules", "That sounds like the right trade-off. You were so drained by the last flat."),
                    new EvalTurn("Mara", "Exactly. I also adopted a rescue dog named Biscuit. He follows me from room to room like a tiny security guard."),
                    new EvalTurn("Jules", "Biscuit is a perfect name. Is he still trying to steal every sock?"),
                    new EvalTurn("Mara", "Always. And I am starting a remote-first role at Northwind Labs this week. I need a stable routine for deep work."),
                    new EvalTurn("Jules", "Good idea. Does your schedule still let you keep mornings calm?"),
                    new EvalTurn("Mara", "Yes, I work best without early meeting pressure. I am protecting my first two hours for focused writing and design work.")
                ]),
                new EvalSession("2025-02-14",
                [
                    new EvalTurn("Mara", "I switched to a mostly plant-based diet, mostly for energy and digestion, but I still keep a Friday cheat meal for pizza."),
                    new EvalTurn("Jules", "That is a solid balance. What did the first two weeks feel like?"),
                    new EvalTurn("Mara", "Better, honestly. Even the afternoon slump is lighter. Biscuit also got into my basil pot and somehow managed to look innocent."),
                    new EvalTurn("Jules", "That is such a Biscuit move. What is your favorite quick lunch now?"),
                    new EvalTurn("Mara", "A tofu grain bowl with roasted carrots and tahini. I keep it on repeat because it is fast and filling."),
                    new EvalTurn("Jules", "Good call. It is the kind of meal that keeps a workday moving."),
                    new EvalTurn("Mara", "Right, and I also decided to stop taking breakfast meetings unless they are truly critical.")
                ]),
                new EvalSession("2025-04-02",
                [
                    new EvalTurn("Mara", "I am finally planning the trip I wanted for months. Portland first, then Lisbon in June, if work stays manageable."),
                    new EvalTurn("Jules", "Portland is such a good start. Are you keeping the Lisbon part?"),
                    new EvalTurn("Mara", "I want a quiet hotel with a balcony and a neighborhood walkable enough for late lunches. Lisbon still sounds lovely, but I do not want to rush it."),
                    new EvalTurn("Jules", "That sounds very you. Which part of the city are you thinking about?"),
                    new EvalTurn("Mara", "I want to stay somewhere close to a tram stop and not too close to nightlife. I value sleep and nice coffee more than loud nights out."),
                    new EvalTurn("Jules", "That is a great rule. A slow trip is often better than a packed one."),
                    new EvalTurn("Mara", "Exactly. I can already picture the mornings: coffee on the balcony, a walk, and no rushing.")
                ]),
                new EvalSession("2025-06-18",
                [
                    new EvalTurn("Mara", "Work got busier than expected, so I canceled the Lisbon leg. I am still going to Portland next month, but only for a few nights."),
                    new EvalTurn("Jules", "That is a sensible call. I am glad you protected your energy instead of burning yourself out."),
                    new EvalTurn("Mara", "Thanks. The apartment is working well for me too. The light is better in the morning and I finally have a small desk by the window where I can think without the room feeling cramped."),
                    new EvalTurn("Jules", "That kind of setup really matters when you need focus."),
                    new EvalTurn("Mara", "It does. Biscuit still sleeps under the desk, but now he steals my pens instead of the basil pot."),
                    new EvalTurn("Jules", "That sounds like progress."),
                    new EvalTurn("Mara", "Very small progress, but still progress.")
                ])
            ]),
        new EvalConversation(
            Id: "leo-and-ramona",
            SpeakerA: "Leo",
            SpeakerB: "Ramona",
            Sessions:
            [
                new EvalSession("2025-01-27",
                [
                    new EvalTurn("Leo", "I signed up for the spring half-marathon in May. Training starts this week and I am trying to be consistent for once."),
                    new EvalTurn("Ramona", "That is great. What is the plan?"),
                    new EvalTurn("Leo", "Three morning runs a week, plus a long weekend run every Saturday. I am trying to keep it sustainable rather than overdo it."),
                    new EvalTurn("Ramona", "Sound strategy. Are you still trying to keep your weekend mornings free?"),
                    new EvalTurn("Leo", "Yes, that part is non-negotiable. My family visit is in Seattle in early June and I do not want to burn out before then."),
                    new EvalTurn("Ramona", "That makes sense. A strong plan beats motivation alone."),
                    new EvalTurn("Leo", "Exactly. I have been taking my route by the river because it is easier on my knees.")
                ]),
                new EvalSession("2025-03-16",
                [
                    new EvalTurn("Leo", "I ran a ten-kilometer test and my shin started hurting after mile six. I had to cut it short and switch to bike intervals for a while."),
                    new EvalTurn("Ramona", "That is frustrating, but smart to back off. Did you lean into the rest?"),
                    new EvalTurn("Leo", "I did. I am cross-training twice a week and doing mobility work before bed. I also stopped eating red meat most days after the trainer suggested a lighter recovery diet."),
                    new EvalTurn("Ramona", "That is a practical adjustment. Any improvement?"),
                    new EvalTurn("Leo", "Some. I am not racing yet, but I am feeling more stable and less stiff."),
                    new EvalTurn("Ramona", "That is exactly what you want before a big event."),
                    new EvalTurn("Leo", "Right. I am trying to keep the goal realistic and protect long-term training."),
                    new EvalTurn("Ramona", "That is the mature approach.")
                ]),
                new EvalSession("2025-05-11",
                [
                    new EvalTurn("Leo", "The half-marathon is in two weeks, and my taper is working. I ran a shorter route last weekend and felt strong, almost completely pain-free."),
                    new EvalTurn("Ramona", "That is exactly what you wanted to hear. Are you still going to Seattle after the race?"),
                    new EvalTurn("Leo", "Yes, I am taking the train out there right after. My cousins are hosting a barbecue, and my sister is already telling me to bring extra snacks."),
                    new EvalTurn("Ramona", "A post-race trip sounds like a nice reward."),
                    new EvalTurn("Leo", "It will be. I have also been keeping my recovery meals simple — rice bowls, fruit, and more vegetables than before."),
                    new EvalTurn("Ramona", "That fits the plan well."),
                    new EvalTurn("Leo", "It does. I am much more deliberate than I used to be.")
                ]),
                new EvalSession("2025-06-08",
                [
                    new EvalTurn("Leo", "The Seattle trip was great. I ran the race in a respectable time and then spent the next two days eating grilled food and walking around the waterfront."),
                    new EvalTurn("Ramona", "That sounds like a perfect recovery week."),
                    new EvalTurn("Leo", "It was. I have now shifted to an easier base-building block and I am planning a quieter summer with more consistent rest days."),
                    new EvalTurn("Ramona", "That sounds like the right reset after the training cycle."),
                    new EvalTurn("Leo", "Exactly. The race was a success, but I do not want to chase intensity again immediately."),
                    new EvalTurn("Ramona", "That kind of discipline is what keeps people going."),
                    new EvalTurn("Leo", "I am learning that the hard way.")
                ])
            ])
    ];

    internal static IReadOnlyList<EvalQuestion> Questions() =>
    [
        new EvalQuestion("mara-sh-1", "mara-and-jules", CategorySingleHop,
            "What is Mara's dog called?",
            "Biscuit.",
            ["biscuit"]),
        new EvalQuestion("mara-sh-2", "mara-and-jules", CategorySingleHop,
            "What kind of work schedule does Mara prefer for deep work?",
            "She prefers a quiet morning routine with no early meetings and two protected hours for focused writing and design work.",
            ["no early meetings", "focused writing", "deep work"]),
        new EvalQuestion("mara-sh-3", "mara-and-jules", CategorySingleHop,
            "What is Mara's preferred quick lunch after switching to a mostly plant-based diet?",
            "A tofu grain bowl with roasted carrots and tahini.",
            ["tofu grain bowl", "roasted carrots", "tahini"]),
        new EvalQuestion("mara-sh-4", "mara-and-jules", CategorySingleHop,
            "What travel preference did Mara express for Lisbon?",
            "She wanted a quiet hotel with a balcony, near a tram stop, and in a neighborhood that avoids nightlife noise.",
            ["quiet hotel", "balcony", "tram stop"]),
        new EvalQuestion("mara-mh-1", "mara-and-jules", CategoryMultiHop,
            "How did Mara's apartment move change her work style and daily routine?",
            "It gave her a quieter second-floor home, better morning light, and a desk by the window so she could protect deep work without street noise or cramped space.",
            ["second-floor", "desk by the window", "deep work"]),
        new EvalQuestion("mara-mh-2", "mara-and-jules", CategoryMultiHop,
            "How did Mara's diet and work routine connect to the way she wants to travel?",
            "She likes calm, stable habits and avoids early pressure, so she wants slower travel with mornings on a balcony, good coffee, and no busy nightlife or rushed plans.",
            ["calm", "balcony", "coffee", "slow travel"]),
        new EvalQuestion("mara-t-1", "mara-and-jules", CategoryTemporal,
            "How did Mara's travel plan change from the original plan to the final decision?",
            "She moved from wanting Portland plus Lisbon to keeping only Portland because work got busier and she canceled the Lisbon leg to protect her energy.",
            ["portland", "lisbon", "canceled"]),
        new EvalQuestion("mara-t-2", "mara-and-jules", CategoryTemporal,
            "What changed in Mara's food habits over time, and what still remained?",
            "She adopted a mostly plant-based diet and cut breakfast meetings, but still kept a Friday pizza cheat meal and a simple lunch routine.",
            ["plant-based", "friday pizza", "cheat meal"]),
        new EvalQuestion("mara-a-1", "mara-and-jules", CategoryAdversarial,
            "What is Mara's exact salary at Northwind Labs?",
            "The conversation never mentions her salary.",
            []),
        new EvalQuestion("mara-a-2", "mara-and-jules", CategoryAdversarial,
            "Did Biscuit win a dog show?",
            "The conversation only mentions him stealing socks and sleeping under the desk; no dog show is discussed.",
            []),
        new EvalQuestion("mara-a-3", "mara-and-jules", CategoryAdversarial,
            "Which neighborhood in Lisbon did Mara choose for her trip?",
            "She never decided on a Lisbon neighborhood, only a general quiet and low-noise preference.",
            []),
        new EvalQuestion("leo-sh-1", "leo-and-ramona", CategorySingleHop,
            "What event is Leo training for?",
            "The spring half-marathon in May.",
            ["half-marathon", "may"]),
        new EvalQuestion("leo-sh-2", "leo-and-ramona", CategorySingleHop,
            "How many morning runs does Leo plan to do each week?",
            "Three morning runs a week.",
            ["three", "morning runs"]),
        new EvalQuestion("leo-sh-3", "leo-and-ramona", CategorySingleHop,
            "What change did Leo make after his shin pain?",
            "He switched to bike intervals and added mobility work, while reducing red meat in his recovery diet.",
            ["bike intervals", "mobility", "red meat"]),
        new EvalQuestion("leo-sh-4", "leo-and-ramona", CategorySingleHop,
            "Where did Leo go after the half-marathon?",
            "Seattle, to visit family and cousins after the race.",
            ["seattle", "family"]),
        new EvalQuestion("leo-mh-1", "leo-and-ramona", CategoryMultiHop,
            "How did Leo's training adaptation help him prepare for the race and the travel afterward?",
            "He cut back to sustainable training, used bike cross-training and mobility, and after the race went to Seattle refreshed and recovered instead of overtraining.",
            ["cross-training", "recovery", "seattle"]),
        new EvalQuestion("leo-mh-2", "leo-and-ramona", CategoryMultiHop,
            "What does Leo's post-race recovery routine suggest about his long-term approach to fitness?",
            "He values sustainable progress, rest, and consistency over chasing intensity too quickly.",
            ["rest", "consistency", "sustainable"]),
        new EvalQuestion("leo-t-1", "leo-and-ramona", CategoryTemporal,
            "How did Leo's diet and training volume evolve throughout the season?",
            "He moved from a more intense plan to a lighter, more sustainable routine with bike work, mobility, and a reduced-red-meat recovery approach, then kept a quieter base-building phase after the race.",
            ["lighter", "reduced red meat", "base-building"]),
        new EvalQuestion("leo-t-2", "leo-and-ramona", CategoryTemporal,
            "What happened to Leo's training status after the race and before the Seattle trip?",
            "He felt strong and nearly pain-free, then went to Seattle for a recovery trip with family and food after the event.",
            ["pain-free", "seattle", "recovery"]),
        new EvalQuestion("leo-a-1", "leo-and-ramona", CategoryAdversarial,
            "What exact time did Leo finish the half-marathon?",
            "The conversation never gives a finish time.",
            []),
        new EvalQuestion("leo-a-2", "leo-and-ramona", CategoryAdversarial,
            "Did Leo win the race?",
            "The story only says he ran it respectably and completed it, not that he won.",
            []),
        new EvalQuestion("leo-a-3", "leo-and-ramona", CategoryAdversarial,
            "What is the name of Leo's coach?",
            "The conversations never mention a coach.",
            [])
    ];

    internal static EvaluationDatasetSnapshot LoadSnapshot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new EvaluationDatasetSnapshot("Built-in fictional LOCOMO-style fixture", Load(), Questions(), Categories);
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Evaluation dataset was not found: {fullPath}", fullPath);
        var dataset = JsonSerializer.Deserialize<DatasetFile>(File.ReadAllText(fullPath), JsonOptions)
            ?? throw new InvalidDataException("Evaluation dataset JSON is empty or invalid.");
        var conversations = (IReadOnlyList<EvalConversation>)(dataset.Conversations ?? []);
        var questions = (IReadOnlyList<EvalQuestion>)(dataset.Questions ?? []);
        Validate(conversations, questions);
        var categories = questions.Select(question => question.Category).Distinct(StringComparer.Ordinal).ToArray();
        return new EvaluationDatasetSnapshot(dataset.Name ?? Path.GetFileNameWithoutExtension(fullPath), conversations, questions, categories);
    }

    private static void Validate(IReadOnlyList<EvalConversation> conversations, IReadOnlyList<EvalQuestion> questions)
    {
        if (conversations.Count == 0) throw new InvalidDataException("Evaluation dataset must contain at least one conversation.");
        if (questions.Count == 0) throw new InvalidDataException("Evaluation dataset must contain at least one question.");
        var conversationIds = conversations.Select(conversation => conversation.Id).ToArray();
        if (conversationIds.Distinct(StringComparer.Ordinal).Count() != conversationIds.Length) throw new InvalidDataException("Evaluation conversation IDs must be unique.");
        if (questions.Select(question => question.Id).Distinct(StringComparer.Ordinal).Count() != questions.Count) throw new InvalidDataException("Evaluation question IDs must be unique.");
        if (questions.Any(question => !conversationIds.Contains(question.ConversationId, StringComparer.Ordinal))) throw new InvalidDataException("Every evaluation question must reference an existing conversation.");
        if (questions.Any(question => string.IsNullOrWhiteSpace(question.Category))) throw new InvalidDataException("Every evaluation question must specify a category.");
    }

    private sealed class DatasetFile
    {
        public string? Name { get; set; }
        public List<EvalConversation>? Conversations { get; set; }
        public List<EvalQuestion>? Questions { get; set; }
    }
}

internal sealed record EvaluationDatasetSnapshot(
    string Name,
    IReadOnlyList<EvalConversation> Conversations,
    IReadOnlyList<EvalQuestion> Questions,
    IReadOnlyList<string> Categories);

internal sealed record EvalConversation(
    string Id,
    string SpeakerA,
    string SpeakerB,
    IReadOnlyList<EvalSession> Sessions);

internal sealed record EvalSession(string Date, IReadOnlyList<EvalTurn> Turns);

internal sealed record EvalTurn(string Speaker, string Text);

internal sealed record EvalQuestion(
    string Id,
    string ConversationId,
    string Category,
    string Question,
    string ExpectedAnswer,
    IReadOnlyList<string> Evidence)
{
    internal bool IsAdversarial => Category == EvaluationDataset.CategoryAdversarial;
}
