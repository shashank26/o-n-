using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PipelineArchitect.Data;
using PipelineArchitect.Simulation;
using PipelineArchitect.Simulation.Machines;
using PipelineArchitect.Simulation.Pipeline;
using Xunit;

namespace PipelineArchitect.Tests;

/// <summary>
/// Decoupled automated verification for Pipeline Architect's graph-driven simulation layer.
/// Proves the player graph controls computation — no LevelData.InputStream → BubbleSort bypass.
/// </summary>
public sealed class PipelineArchitectTests : IDisposable
{
    private const string Intern01LevelFileName = "intern_01_bubblesort.json";

    public PipelineArchitectTests()
    {
        SimulationEvents.ClearAllSubscribers();
    }

    public void Dispose()
    {
        SimulationEvents.ClearAllSubscribers();
    }

    [Fact]
    public void LevelLoading_CompleteAndMalformedJson_ParseAsExpected()
    {
        string levelPath = ResolveLevelPath(Intern01LevelFileName);
        Assert.True(File.Exists(levelPath), $"Expected level profile at: {levelPath}");

        LevelData level = LevelDataLoader.FromFile(levelPath);

        Assert.NotNull(level);
        Assert.Equal("intern_01", level.LevelId);
        Assert.Equal(LevelTier.Intern, level.Tier);
        Assert.Equal("Sort the Shipment", level.Title);
        Assert.False(string.IsNullOrWhiteSpace(level.Description));
        Assert.Equal(new[] { 5, 1, 4, 2, 8 }, level.InputStream);
        Assert.Equal(new[] { 1, 2, 4, 5, 8 }, level.ExpectedOutput);
        Assert.Equal(30, level.Budgets.MaxCPUCycles);
        Assert.Equal(2, level.Budgets.MaxRAMSlots);
        Assert.Contains("Bubble_Sort_Machine", level.AllowedToolbox);
        Assert.Equal(VisualSkinIds.ToyBricks, level.VisualSkin);
        Assert.NotNull(level.OptimalCreator);
        Assert.Equal("Merge Sort", level.OptimalCreator!.AlgorithmName);
        Assert.NotNull(level.TutorialSteps);
        Assert.Equal(5, level.TutorialSteps!.Count);
        Assert.Contains("Bubble Sort", level.TutorialSteps[0].Message);
        Assert.Contains("Execute Factory", level.TutorialSteps[3].Message);
        Assert.NotNull(level.InteractiveChallenge);
        Assert.True(level.InteractiveChallenge!.Enabled);
        Assert.Equal("adjacent_sort_discovery", level.InteractiveChallenge.ChallengeId);
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, level.InteractiveChallenge.InitialValues);

        bool parsed = LevelDataLoader.TryFromJson("{ not valid json", out LevelData? malformed, out string errorMessage);
        Assert.False(parsed);
        Assert.Null(malformed);
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));

        Assert.Throws<JsonException>(() => LevelDataLoader.FromJson("""{"levelId":""}"""));
    }

    /// <summary>Test 1 — Valid Bubble Sort pipeline succeeds with events and sorted output from BubbleSort.</summary>
    [Fact]
    public void ValidBubbleSortPipeline_SortsAndRaisesExecutionEvents()
    {
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath(Intern01LevelFileName));
        int n = level.InputStream.Count;
        int expectedComparisons = n * (n - 1) / 2;

        var started = new List<string>();
        var completed = new List<string>();
        var produced = new List<string>();
        var steps = new List<string>();
        bool? endedSuccessfully = null;

        SimulationEvents.OnNodeExecutionStarted += id => started.Add(id);
        SimulationEvents.OnNodeExecutionCompleted += id => completed.Add(id);
        SimulationEvents.OnDataStreamProduced += (id, _, _, _) => produced.Add(id);
        SimulationEvents.OnNodeExecutionStep += (_, kind, _) => steps.Add(kind);
        SimulationEvents.OnElementCompared += (_, _) => { };
        SimulationEvents.OnElementsSwapped += (_, _) => { };
        SimulationEvents.OnSimulationEnded += (ok, _) => endedSuccessfully = ok;

        var engine = new SimulationEngine(level);
        SimulationResult result = engine.RunLinearBubbleSortPipeline();

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(SimulationResultState.Success, result.State);
        Assert.Equal(level.ExpectedOutput, result.FinalOutput);
        Assert.Equal(expectedComparisons, result.CpuCyclesConsumed);
        Assert.Equal("Engine_Bubble_Sort_Machine", result.ProducingNodeId);
        Assert.Contains(PipelineNodeIds.InputStream, started);
        Assert.Contains("Engine_Bubble_Sort_Machine", started);
        Assert.Contains(PipelineNodeIds.OutputSink, started);
        Assert.Contains("Engine_Bubble_Sort_Machine", produced);
        Assert.Contains("Started", steps);
        Assert.Contains("Compared", steps);
        Assert.Contains("Swapped", steps);
        Assert.Contains("Completed", steps);
        Assert.Contains("DataStreamProduced", steps);
        Assert.NotNull(result.Trace);
        Assert.Contains(result.Trace!.Entries, e => e.StepKind == "Compared");
        Assert.Contains(result.Trace.Entries, e => e.StepKind == "Validation" && e.Detail == "SUCCESS");
        Assert.True(endedSuccessfully);
    }

    /// <summary>Test 2 — Without BubbleSort the engine must not silently sort.</summary>
    [Fact]
    public void GraphWithoutBubbleSort_DoesNotSilentlySort()
    {
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath(Intern01LevelFileName));
        var graph = new PipelineGraph();
        graph.AddNode(new InputStreamNode(PipelineNodeIds.InputStream, level.InputStream));
        graph.AddNode(new OutputSinkNode(PipelineNodeIds.OutputSink));
        graph.AddEdge(new PipelineEdge
        {
            EdgeId = "e0",
            FromNodeId = PipelineNodeIds.InputStream,
            FromPortId = PortId.Out,
            ToNodeId = PipelineNodeIds.OutputSink,
            ToPortId = PortId.In
        });

        var engine = new SimulationEngine(level);
        SimulationResult result = engine.ExecuteGraphAsync(graph, TimeSpan.Zero).GetAwaiter().GetResult();

        Assert.False(result.IsSuccess);
        Assert.Equal(SimulationResultState.FailedOutputMismatch, result.State);
        Assert.Equal(level.InputStream, result.FinalOutput);
        Assert.NotEqual(level.ExpectedOutput, result.FinalOutput);
    }

    /// <summary>Test 3 — BubbleSort with no incoming stream blocks at BubbleSort.</summary>
    [Fact]
    public void MissingInput_BlocksAtBubbleSort()
    {
        var machine = new BubbleSortMachineNode("Engine_Bubble_Sort_Machine");
        var context = new NodeExecutionContext
        {
            NodeId = machine.NodeId,
            Inputs = new Dictionary<PortId, DataStream>(),
            Trace = new ExecutionTrace(),
            TryConsumeCpu = _ => true,
            TryAllocateRam = (_, _) => true,
            ReleaseRam = () => { },
            GetCpuCycles = () => 0,
            GetRamSlots = () => 0,
            GetPeakRamSlots = () => 0,
            RaiseCompared = (_, _) => { },
            RaiseSwapped = (_, _) => { },
            YieldAsync = (_, _) => System.Threading.Tasks.Task.CompletedTask,
            StepDelay = TimeSpan.Zero
        };

        NodeExecutionResult result = machine.ExecuteAsync(context).GetAwaiter().GetResult();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Equal("Engine_Bubble_Sort_Machine", result.Failure!.NodeId);
        Assert.Equal(ExecutionFailureType.MissingInput, result.Failure.FailureType);

        // Graph-level: disconnected BubbleSort is also identified.
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath(Intern01LevelFileName));
        var graph = new PipelineGraph();
        graph.AddNode(new InputStreamNode(PipelineNodeIds.InputStream, level.InputStream));
        graph.AddNode(new BubbleSortMachineNode("Engine_Bubble_Sort_Machine"));
        graph.AddNode(new OutputSinkNode(PipelineNodeIds.OutputSink));
        graph.AddEdge(new PipelineEdge
        {
            EdgeId = "e0",
            FromNodeId = PipelineNodeIds.InputStream,
            FromPortId = PortId.Out,
            ToNodeId = PipelineNodeIds.OutputSink,
            ToPortId = PortId.In
        });

        PipelineGraphValidator.ValidationResult validation = PipelineGraphValidator.Validate(graph);
        Assert.False(validation.IsValid);
        Assert.Equal("Engine_Bubble_Sort_Machine", validation.Failure!.NodeId);
        Assert.Equal(ExecutionFailureType.UnreachableNode, validation.Failure.FailureType);
    }

    /// <summary>Test 4 — BubbleSort without Output connection does not succeed.</summary>
    [Fact]
    public void MissingOutput_DoesNotSucceed()
    {
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath(Intern01LevelFileName));
        var graph = new PipelineGraph();
        graph.AddNode(new InputStreamNode(PipelineNodeIds.InputStream, level.InputStream));
        graph.AddNode(new BubbleSortMachineNode("Engine_Bubble_Sort_Machine"));
        graph.AddNode(new OutputSinkNode(PipelineNodeIds.OutputSink));
        graph.AddEdge(new PipelineEdge
        {
            EdgeId = "e0",
            FromNodeId = PipelineNodeIds.InputStream,
            FromPortId = PortId.Out,
            ToNodeId = "Engine_Bubble_Sort_Machine",
            ToPortId = PortId.In
        });

        var engine = new SimulationEngine(level);
        SimulationResult result = engine.ExecuteGraphAsync(graph, TimeSpan.Zero).GetAwaiter().GetResult();

        Assert.False(result.IsSuccess);
        Assert.Equal(SimulationResultState.FailedInvalidPipeline, result.State);
        Assert.NotNull(result.Failure);
        Assert.Equal(ExecutionFailureType.UnreachableNode, result.Failure!.FailureType);
    }

    /// <summary>Test 5 — Incompatible port types are rejected by validation.</summary>
    [Fact]
    public void InvalidPortTypes_RejectedByValidation()
    {
        var graph = new PipelineGraph();
        graph.AddNode(new InputStreamNode(PipelineNodeIds.InputStream, new[] { 1, 2 }));
        graph.AddNode(new OutputSinkNode(PipelineNodeIds.OutputSink));
        graph.AddNode(new StubGraphTypedNode("GraphMachine"));
        graph.AddEdge(new PipelineEdge
        {
            EdgeId = "e0",
            FromNodeId = PipelineNodeIds.InputStream,
            FromPortId = PortId.Out,
            ToNodeId = "GraphMachine",
            ToPortId = PortId.In
        });
        graph.AddEdge(new PipelineEdge
        {
            EdgeId = "e1",
            FromNodeId = "GraphMachine",
            FromPortId = PortId.Out,
            ToNodeId = PipelineNodeIds.OutputSink,
            ToPortId = PortId.In
        });

        PipelineGraphValidator.ValidationResult validation = PipelineGraphValidator.Validate(graph);
        Assert.False(validation.IsValid);
        Assert.Equal(ExecutionFailureType.IncompatibleType, validation.Failure!.FailureType);
    }

    /// <summary>Test 6 — Execution trace contains the required step kinds.</summary>
    [Fact]
    public void ExecutionTrace_ContainsRequiredSteps()
    {
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath(Intern01LevelFileName));
        var engine = new SimulationEngine(level);
        SimulationResult result = engine.RunLinearBubbleSortPipeline();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Trace);
        string[] kinds = result.Trace!.Entries.Select(e => e.StepKind).ToArray();
        Assert.Contains("Started", kinds);
        Assert.Contains("Compared", kinds);
        Assert.Contains("Swapped", kinds);
        Assert.Contains("Completed", kinds);
        Assert.Contains("DataStreamProduced", kinds);
        Assert.Contains("Received", kinds);
        Assert.Contains("Validation", kinds);
    }

    /// <summary>Test 7 — CPU budget still enforced during BubbleSort graph execution.</summary>
    [Fact]
    public void ConstraintOverheat_ExceedingMaxCpu_FailsGracefully()
    {
        var overheatLevel = new LevelData
        {
            LevelId = "test_overheat",
            Tier = LevelTier.Intern,
            Title = "Overheat Fixture",
            Description = "Intentionally tiny CPU budget to force failure.",
            InputStream = new List<int> { 3, 2, 1 },
            ExpectedOutput = new List<int> { 1, 2, 3 },
            Budgets = new LevelBudgets
            {
                MaxCPUCycles = 1,
                MaxRAMSlots = 2
            },
            AllowedToolbox = new List<string> { "Bubble_Sort_Machine" }
        };

        bool? endedSuccessfully = null;
        SimulationEvents.OnSimulationEnded += (isSuccess, _) => endedSuccessfully = isSuccess;
        ExecutionFailure? blocked = null;
        SimulationEvents.OnExecutionBlocked += f => blocked = f;

        var engine = new SimulationEngine(overheatLevel);
        SimulationResult result = engine.RunLinearBubbleSortPipeline();

        Assert.False(result.IsSuccess);
        Assert.Equal(SimulationResultState.FailedCpuOverheat, result.State);
        Assert.True(result.CpuCyclesConsumed > overheatLevel.Budgets.MaxCPUCycles);
        Assert.Equal(2, result.CpuCyclesConsumed);
        Assert.NotNull(blocked);
        Assert.Equal(ExecutionFailureType.CpuBudgetExceeded, blocked!.FailureType);
        Assert.Contains("Bubble_Sort", blocked.NodeId, StringComparison.OrdinalIgnoreCase);
        Assert.False(endedSuccessfully);
    }

    /// <summary>Test 8 — Wrong pipeline runs but fails evaluation.</summary>
    [Fact]
    public void WrongPipeline_RunsThenFailsEvaluation()
    {
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath(Intern01LevelFileName));
        var routes = new List<PipelineRouteBinding>
        {
            new()
            {
                SourceNodeId = PipelineNodeIds.InputStream,
                SourcePort = 0,
                TargetNodeId = PipelineNodeIds.OutputSink,
                TargetPort = 0,
                TargetMachineId = string.Empty
            }
        };

        var engine = new SimulationEngine(level);
        engine.SetPipelineConnections(routes);
        SimulationResult result = engine.RunPipeline();

        Assert.False(result.IsSuccess);
        Assert.Equal(SimulationResultState.FailedOutputMismatch, result.State);
        Assert.NotNull(result.Failure);
        Assert.Equal(ExecutionFailureType.EvaluationMismatch, result.Failure!.FailureType);
        Assert.Equal(level.InputStream, result.FinalOutput);
    }

    /// <summary>Empty routes must not invent a BubbleSort success path.</summary>
    [Fact]
    public void EmptyRoutes_DoNotBypassGraph()
    {
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath(Intern01LevelFileName));
        var engine = new SimulationEngine(level);
        SimulationResult result = engine.RunPipeline();

        Assert.False(result.IsSuccess);
        Assert.Equal(SimulationResultState.FailedInvalidPipeline, result.State);
    }

    private static string ResolveLevelPath(string fileName)
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            string? directory = start;
            while (directory is not null)
            {
                string candidate = Path.Combine(directory, "levels", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate '{fileName}' under any ancestor levels/ directory.");
    }

    /// <summary>Test-only machine with Graph-typed ports to exercise type validation.</summary>
    private sealed class StubGraphTypedNode : IPipelineNode
    {
        public StubGraphTypedNode(string nodeId)
        {
            NodeId = nodeId;
            Ports = new[]
            {
                new PortDefinition
                {
                    Id = PortId.In,
                    Direction = PortDirection.Input,
                    DataType = PortDataType.Graph,
                    Role = PortRole.GraphTopology
                },
                new PortDefinition
                {
                    Id = PortId.Out,
                    Direction = PortDirection.Output,
                    DataType = PortDataType.Graph,
                    Role = PortRole.GraphTopology
                }
            };
        }

        public string NodeId { get; }
        public PipelineNodeKind Kind => PipelineNodeKind.Unsupported;
        public IReadOnlyList<PortDefinition> Ports { get; }

        public System.Threading.Tasks.Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context) =>
            System.Threading.Tasks.Task.FromResult(NodeExecutionResult.Ok(new Dictionary<PortId, DataStream>()));
    }
}
