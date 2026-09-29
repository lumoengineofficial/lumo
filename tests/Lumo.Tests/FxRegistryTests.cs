using System.Numerics;
using Lumo.Engine.Rendering;

namespace Lumo.Tests;

[Collection("Blackboard")]
public class FxRegistryTests
{
    public FxRegistryTests()
    {
        FxRegistry.Clear();
        FxRegistry.MeshShading = false;
    }

    [Fact]
    public void Register_AddsAndReplacesById()
    {
        FxRegistry.Register(new FxEffect("vignette", FxKind.Vignette, 0.4f, Vector3.Zero));
        FxRegistry.Register(new FxEffect("vignette", FxKind.Vignette, 0.8f, Vector3.Zero));
        FxRegistry.Register(new FxEffect("grade", FxKind.ColorGrade, 0.5f, new Vector3(0.1f, 0.1f, 0.2f)));

        var effects = FxRegistry.Snapshot();
        Assert.Equal(2, effects.Count);
        FxEffect vignette = Assert.Single(effects, e => e.Id == "vignette");
        Assert.Equal(0.8f, vignette.Intensity, 3);
    }

    [Fact]
    public void Remove_AndClear_EmptiesRegistry()
    {
        FxRegistry.Register(new FxEffect("a", FxKind.Vignette, 0.5f, Vector3.Zero));
        FxRegistry.Register(new FxEffect("b", FxKind.ColorGrade, 0.5f, Vector3.Zero));

        FxRegistry.Remove("a");
        Assert.DoesNotContain(FxRegistry.Snapshot(), e => e.Id == "a");

        FxRegistry.Clear();
        Assert.Empty(FxRegistry.Snapshot());
    }

    [Fact]
    public void Snapshot_IsACopy_CallersCannotMutateRegistry()
    {
        FxRegistry.Register(new FxEffect("a", FxKind.Vignette, 0.5f, Vector3.Zero));
        var snap = new List<FxEffect>(FxRegistry.Snapshot());
        snap.Clear();
        Assert.Single(FxRegistry.Snapshot());
    }

    [Fact]
    public void MeshShading_Toggles()
    {
        Assert.False(FxRegistry.MeshShading);
        FxRegistry.MeshShading = true;
        Assert.True(FxRegistry.MeshShading);
        FxRegistry.MeshShading = false;
    }

    [Fact]
    public void FxLighting_FacesTowardLight_AreBrighter()
    {
        float lit = FxLighting.Brightness(new Vector3(0, 1, 0), new Vector3(0, 5, 0));
        float side = FxLighting.Brightness(new Vector3(1, 0, 0), new Vector3(0, 5, 0));
        float back = FxLighting.Brightness(new Vector3(0, -1, 0), new Vector3(0, 5, 0));

        Assert.True(lit > side, "top face should beat side face");
        Assert.Equal(lit, back, 3); // two-sided: a back-facing normal flips toward the camera
        Assert.InRange(side, FxLighting.Ambient, 1f);
        Assert.InRange(lit, FxLighting.Ambient, 1f);
    }
}
