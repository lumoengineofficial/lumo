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

    [Fact]
    public void SerializeDeserialize_PreservesPbrMaterialParams()
    {
        var scene = new Scene { Name = "PBR" };
        Entity box = scene.CreateEntity("GlowBox");
        box.MeshRenderer = new MeshRendererComponent
        {
            MeshName = "Cube",
            Color = new Vector3(0.9f, 0.1f, 0.1f),
            Metallic = 0.85f,
            Roughness = 0.25f,
            Emissive = 0.6f,
            EmissiveColor = new Vector3(1f, 0.5f, 0f),
            NormalMap = "Assets/brick_n.png"
        };

        Scene restored = Scene.Deserialize(scene.Serialize());

        MeshRendererComponent? mr = restored.FindByName("GlowBox")?.MeshRenderer;
        Assert.NotNull(mr);
        Assert.Equal(0.85f, mr!.Metallic, 3);
        Assert.Equal(0.25f, mr.Roughness, 3);
        Assert.Equal(0.6f, mr.Emissive, 3);
        Assert.Equal(new Vector3(1f, 0.5f, 0f), mr.EmissiveColor);
        Assert.Equal("Assets/brick_n.png", mr.NormalMap);
    }

    [Fact]
    public void Deserialize_OldSceneWithoutPbrFields_UsesDefaults()
    {
        // A scene saved before PBR fields existed: no Metallic/Roughness keys.
        const string json = """
        {
          "Name": "Old",
          "Entities": [
            { "Name": "Rock", "Id": 1,
              "Transform": { "Position": [0,0,0], "Rotation": [0,0,0,1], "Scale": [1,1,1] },
              "MeshRenderer": { "MeshName": "Cube", "IsVisible": true, "Color": [0.5,0.6,0.7] } }
          ]
        }
        """;

        MeshRendererComponent? mr = Scene.Deserialize(json).FindByName("Rock")?.MeshRenderer;
        Assert.NotNull(mr);
        Assert.Equal(0f, mr!.Metallic, 3);
        Assert.Equal(0.7f, mr.Roughness, 3);   // component default
        Assert.Equal(0f, mr.Emissive, 3);
        Assert.Equal(Vector3.One, mr.EmissiveColor);
        Assert.Null(mr.NormalMap);
    }
}
