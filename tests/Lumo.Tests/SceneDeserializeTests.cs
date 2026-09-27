using System.Numerics;
using Lumo.Engine.Scene;

namespace Lumo.Tests;

public class SceneDeserializeTests
{
    [Fact]
    public void SerializeDeserialize_PreservesEntitiesAndComponents()
    {
        var scene = new Scene { Name = "RoundTrip" };
        Entity actor = scene.CreateEntity("Hero");
        actor.Transform.Position = new Vector3(1.5f, -2f, 3f);
        actor.SpriteRenderer = new SpriteRendererComponent
        {
            Width = 2,
            Height = 1,
            Color = new Vector3(0.2f, 0.4f, 0.6f),
            IsVisible = true
        };
        Entity cam = scene.CreateEntity("Cam");
        cam.Camera = new CameraComponent { IsPrimary = true, FieldOfView = 75 };

        string json = scene.Serialize();
        Scene restored = Scene.Deserialize(json);

        Assert.Equal("RoundTrip", restored.Name);
        Assert.Equal(2, restored.AllEntities.Count);

        Entity? hero = restored.FindByName("Hero");
        Assert.NotNull(hero);
        Assert.Equal(new Vector3(1.5f, -2f, 3f), hero!.Transform.Position);
        Assert.NotNull(hero.SpriteRenderer);
        Assert.Equal(2f, hero.SpriteRenderer!.Width);
        Assert.True(hero.SpriteRenderer.IsVisible);

        Entity? cam2 = restored.FindByName("Cam");
        Assert.NotNull(cam2?.Camera);
        Assert.True(cam2!.Camera!.IsPrimary);
        Assert.Equal(75f, cam2.Camera.FieldOfView);
        // note: entity Ids are reassigned by the serializer's id counter,
        // so byte-stability across roundtrips is not guaranteed
    }
}
