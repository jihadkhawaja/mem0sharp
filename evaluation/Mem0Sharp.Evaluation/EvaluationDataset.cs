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
            Id: "harbor-coffee",
            SpeakerA: "Rana",
            SpeakerB: "Theo",
            Sessions:
            [
                new EvalSession("2024-01-15",
                [
                    new EvalTurn("Rana", "I finally signed the lease for my pottery studio. It is a small space on Harbor Lane, near the old lighthouse."),
                    new EvalTurn("Theo", "That is wonderful. You have talked about opening a studio for years."),
                    new EvalTurn("Rana", "I have. I want to run weekend wheel-throwing workshops for beginners starting in March."),
                    new EvalTurn("Theo", "Count me in. I still owe you for recommending that physio for my shoulder."),
                    new EvalTurn("Rana", "How is the shoulder?"),
                    new EvalTurn("Theo", "Much better. I am back to swimming three mornings a week at the community pool.")
                ]),
                new EvalSession("2024-02-10",
                [
                    new EvalTurn("Rana", "The studio kilns arrived today, two secondhand electric kilns. My budget is blown but I am happy."),
                    new EvalTurn("Theo", "Worth it. By the way, I adopted a greyhound last weekend. Her name is Pepper."),
                    new EvalTurn("Rana", "Pepper! Bring her by the studio once the glazing dust settles."),
                    new EvalTurn("Theo", "She is retired from racing, so mostly she naps. I also started a new job as a lighthouse tour coordinator, funny enough right near your studio."),
                    new EvalTurn("Rana", "Then we can grab coffee at the Saltbush Cafe between your tours. Their cardamom buns are excellent."),
                    new EvalTurn("Theo", "Deal. I am vegetarian these days, so cardamom buns sound perfect.")
                ]),
                new EvalSession("2024-03-22",
                [
                    new EvalTurn("Rana", "First workshop went well. Eight students, and only one collapsed bowl."),
                    new EvalTurn("Theo", "I told everyone at the lighthouse about it. Three of my coworkers signed up for April."),
                    new EvalTurn("Rana", "That helps. I am teaching every Saturday morning now. Oh, and I decided to glaze everything in deep sea blue as the studio signature."),
                    new EvalTurn("Theo", "Very on brand for Harbor Lane."),
                    new EvalTurn("Rana", "Are you still swimming?"),
                    new EvalTurn("Theo", "I switched to evening swims because the tour shifts start at dawn. Pepper hates being alone in the morning.")
                ]),
                new EvalSession("2024-04-18",
                [
                    new EvalTurn("Rana", "The gallery on Pier Street offered to display my sea-blue glaze collection in June."),
                    new EvalTurn("Theo", "Amazing. I will bring the whole lighthouse crew to the opening."),
                    new EvalTurn("Rana", "I also bought a secondhand cargo bike to haul clay. The van was costing too much."),
                    new EvalTurn("Theo", "Practical. Pepper and I will race you along the harbor path."),
                    new EvalTurn("Rana", "You will lose. How is the tour coordinator job?"),
                    new EvalTurn("Theo", "Busy. Summer season doubles the visitors, and I am training two new guides.")
                ])
            ]),
        new EvalConversation(
            Id: "night-garden",
            SpeakerA: "Jonas",
            SpeakerB: "Priya",
            Sessions:
            [
                new EvalSession("2024-05-03",
                [
                    new EvalTurn("Jonas", "I passed my sommelier exam on the second try. The fortified wines section almost ended me."),
                    new EvalTurn("Priya", "Congratulations! You deserve it after all those flashcards."),
                    new EvalTurn("Jonas", "Thanks. How is the night garden coming along?"),
                    new EvalTurn("Priya", "The moonflowers finally opened. I planted jasmine along the fence this weekend, and the evening scent is incredible."),
                    new EvalTurn("Jonas", "I will bring a bottle of aged tawny port to celebrate when the jasmine peaks."),
                    new EvalTurn("Priya", "Perfect. My rooftop beehive is also thriving; I harvested four jars of honey yesterday.")
                ]),
                new EvalSession("2024-06-14",
                [
                    new EvalTurn("Jonas", "The restaurant promoted me to head sommelier. I start next month and I am rewriting the entire wine list."),
                    new EvalTurn("Priya", "Head sommelier! Will you still have time for our astronomy nights?"),
                    new EvalTurn("Jonas", "I will protect them. Speaking of which, the Perseids peak in August and I booked a cabin near Dark Sky Ridge."),
                    new EvalTurn("Priya", "I am in. I will bring my new telescope; I upgraded to an eight-inch Dobsonian."),
                    new EvalTurn("Jonas", "Fancy. I am still using the binoculars my uncle gave me."),
                    new EvalTurn("Priya", "They got you hooked, though. Oh, I also started volunteering at the botanical garden on Sundays, teaching kids about night-blooming plants.")
                ]),
                new EvalSession("2024-07-09",
                [
                    new EvalTurn("Jonas", "Rewriting the wine list is harder than the exam. I tasted forty wines this week and my palate is wrecked."),
                    new EvalTurn("Priya", "Poor thing. My bees, on the other hand, are productive. Six jars this month."),
                    new EvalTurn("Jonas", "Save me one. Hey, did I tell you my sister is getting married in October? I am in charge of the wedding wines."),
                    new EvalTurn("Priya", "No pressure at all. What is the venue?"),
                    new EvalTurn("Jonas", "A vineyard estate, of course. She met her fiance at a harvest festival."),
                    new EvalTurn("Priya", "Romantic. I promised the botanical garden a honey-and-jasmine workshop for their autumn program.")
                ]),
                new EvalSession("2024-08-20",
                [
                    new EvalTurn("Jonas", "The Perseids from Dark Sky Ridge were unreal. We counted sixty meteors an hour."),
                    new EvalTurn("Priya", "The Dobsonian earned its keep. I also got the clearest view of Saturn's rings I have ever had."),
                    new EvalTurn("Jonas", "Worth every mosquito bite. My wine list launches Friday, by the way. Ninety bottles."),
                    new EvalTurn("Priya", "Ninety! From forty tastings to ninety bottles is quite a journey."),
                    new EvalTurn("Jonas", "Tell me about it. I am taking a week off after the launch, just sleeping and hiking."),
                    new EvalTurn("Priya", "You have earned it. The moonflowers are almost done for the season, but the jasmine is still going strong.")
                ])
            ])
    ];

    internal static IReadOnlyList<EvalQuestion> Questions() =>
    [
        // harbor-coffee: single-hop
        new EvalQuestion("hc-sh-1", "harbor-coffee", CategorySingleHop,
            "Where is Rana's pottery studio located?",
            "On Harbor Lane, near the old lighthouse.",
            ["harbor lane", "lighthouse"]),
        new EvalQuestion("hc-sh-2", "harbor-coffee", CategorySingleHop,
            "What kind of dog did Theo adopt, and what is its name?",
            "A retired racing greyhound named Pepper.",
            ["greyhound", "pepper"]),
        new EvalQuestion("hc-sh-3", "harbor-coffee", CategorySingleHop,
            "What is the signature glaze color of Rana's studio collection?",
            "Deep sea blue.",
            ["sea blue", "deep sea"]),
        new EvalQuestion("hc-sh-4", "harbor-coffee", CategorySingleHop,
            "What job does Theo have?",
            "He is a lighthouse tour coordinator.",
            ["lighthouse", "tour coordinator"]),
        // harbor-coffee: multi-hop
        new EvalQuestion("hc-mh-1", "harbor-coffee", CategoryMultiHop,
            "What connects the location of Theo's workplace to Rana's studio?",
            "The lighthouse Theo works at is near Rana's pottery studio on Harbor Lane, so they work close to each other.",
            ["lighthouse", "harbor lane"]),
        new EvalQuestion("hc-mh-2", "harbor-coffee", CategoryMultiHop,
            "Why did Theo suggest meeting at the Saltbush Cafe, and what does he like to eat there that fits his diet?",
            "The cafe sits between his lighthouse tours and Rana's studio, and as a vegetarian he enjoys their cardamom buns.",
            ["saltbush", "cardamom", "vegetarian"]),
        new EvalQuestion("hc-mh-3", "harbor-coffee", CategoryMultiHop,
            "Which two purchases show Rana investing in and then economizing on her pottery business?",
            "She bought two secondhand electric kilns, then switched from a costly van to a secondhand cargo bike to haul clay.",
            ["kiln", "cargo bike"]),
        // harbor-coffee: temporal
        new EvalQuestion("hc-t-1", "harbor-coffee", CategoryTemporal,
            "How did Theo's swimming routine change over time, and why?",
            "He swam three mornings a week at the community pool, then switched to evening swims because his dawn tour shifts and Pepper's dislike of morning loneliness conflicted with it.",
            ["morning", "evening", "tour"]),
        new EvalQuestion("hc-t-2", "harbor-coffee", CategoryTemporal,
            "How did Rana's workshop schedule evolve after the first workshop?",
            "After a first workshop with eight students in March, she began teaching every Saturday morning.",
            ["eight", "saturday"]),
        // harbor-coffee: adversarial
        new EvalQuestion("hc-a-1", "harbor-coffee", CategoryAdversarial,
            "How much does Rana charge for a weekend workshop?",
            "The conversations never mention workshop pricing.",
            []),
        new EvalQuestion("hc-a-2", "harbor-coffee", CategoryAdversarial,
            "What did Theo study at university?",
            "The conversations never mention Theo's education.",
            []),
        new EvalQuestion("hc-a-3", "harbor-coffee", CategoryAdversarial,
            "Did Rana's gallery exhibition in June sell out?",
            "The conversations end before the June exhibition, so the outcome is unknown.",
            []),
        // night-garden: single-hop
        new EvalQuestion("ng-sh-1", "night-garden", CategorySingleHop,
            "What exam did Jonas pass, and on which attempt?",
            "The sommelier exam, on his second try.",
            ["sommelier", "second"]),
        new EvalQuestion("ng-sh-2", "night-garden", CategorySingleHop,
            "What telescope does Priya use for astronomy nights?",
            "An eight-inch Dobsonian.",
            ["dobsonian", "eight-inch", "eight inch"]),
        new EvalQuestion("ng-sh-3", "night-garden", CategorySingleHop,
            "Where does Priya volunteer and what does she teach?",
            "At the botanical garden on Sundays, teaching kids about night-blooming plants.",
            ["botanical garden", "night-blooming"]),
        new EvalQuestion("ng-sh-4", "night-garden", CategorySingleHop,
            "What is Jonas in charge of for his sister's October wedding?",
            "The wedding wines, at a vineyard estate.",
            ["wine"]),
        // night-garden: multi-hop
        new EvalQuestion("ng-mh-1", "night-garden", CategoryMultiHop,
            "Which of Priya's garden products does she plan to feature in her autumn botanical garden workshop?",
            "Honey from her rooftop beehive and jasmine from her night garden.",
            ["honey", "jasmine"]),
        new EvalQuestion("ng-mh-2", "night-garden", CategoryMultiHop,
            "What did Jonas promise to bring when Priya's jasmine peaks, and how does it relate to his profession?",
            "A bottle of aged tawny port, fitting his work as a sommelier.",
            ["tawny", "port", "sommelier"]),
        new EvalQuestion("ng-mh-3", "night-garden", CategoryMultiHop,
            "How did the cabin trip to Dark Sky Ridge combine both friends' interests?",
            "They watched the Perseid meteor shower, where Priya's Dobsonian telescope gave her a clear view of Saturn's rings while they counted sixty meteors an hour.",
            ["perseid", "saturn", "dobsonian"]),
        // night-garden: temporal
        new EvalQuestion("ng-t-1", "night-garden", CategoryTemporal,
            "How did Jonas's role at the restaurant change, and how large is the wine list he launched?",
            "He was promoted to head sommelier and launched a ninety-bottle wine list.",
            ["head sommelier", "ninety"]),
        new EvalQuestion("ng-t-2", "night-garden", CategoryTemporal,
            "How did Priya's honey harvest change between May and June?",
            "It grew from four jars in May to six jars in June.",
            ["four", "six"]),
        // night-garden: adversarial
        new EvalQuestion("ng-a-1", "night-garden", CategoryAdversarial,
            "Which wine did the sister choose for the wedding toast?",
            "The conversations only say Jonas is in charge of the wines; no specific wedding wine is chosen.",
            []),
        new EvalQuestion("ng-a-2", "night-garden", CategoryAdversarial,
            "What is the name of the restaurant where Jonas works?",
            "The conversations never name the restaurant.",
            []),
        new EvalQuestion("ng-a-3", "night-garden", CategoryAdversarial,
            "Did Priya's bees survive the winter?",
            "The conversations end in August, before any winter, so this is unknown.",
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
