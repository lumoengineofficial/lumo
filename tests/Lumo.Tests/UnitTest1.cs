using Lumo.Engine.Core;
using Lumo.Engine.Scene;
using Lumo.Engine.Rendering.Abstractions;
using System.Numerics;

namespace Lumo.Tests;

public class EngineCoreTests
{
    [Fact]
    public void Engine_CreatesSuccessfully()
    {
        using var engine = new LumoEngine();
        Assert.NotNull(engine);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void Engine_Initializes()
    {
        using var engine = new LumoEngine();
        engine.Initialize();
        Assert.NotNull(engine.Logger);
        Assert.NotNull(engine.Time);
        Assert.NotNull(engine.Events);
    }

    [Fact]
    public void Engine_StartStop()
    {
        using var engine = new LumoEngine();
        engine.Initialize();

        engine.Start();
        Assert.True(engine.IsRunning);

        engine.Stop();
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void TimeManager_UpdateTracksFrameCount()
    {
        using var engine = new LumoEngine();
        engine.Initialize();
        engine.Start();

        engine.Tick();
        engine.Tick();
        engine.Tick();

        Assert.True(engine.Time.FrameCount >= 3);
    }

    [Fact]
    public void EventBus_SubscribeAndRaise()
    {
        using var bus = new EventBus();
        bool received = false;
        bus.Subscribe<EngineEvents.Tick>(_ => received = true);

        bus.Raise(new EngineEvents.Tick(0.016f));
        Assert.True(received);
    }

    [Fact]
    public void EventBus_UnsubscribeOnDispose()
    {
        using var bus = new EventBus();
        int count = 0;
        var sub = bus.Subscribe<EngineEvents.Tick>(_ => count++);

        bus.Raise(new EngineEvents.Tick(0.016f));
        Assert.Equal(1, count);

        sub.Dispose();
        bus.Raise(new EngineEvents.Tick(0.016f));
        Assert.Equal(1, count); // Should not increment
    }
}

public class SceneTests
{
    [Fact]
    public void Scene_CreatesEntity()
    {
        var scene = new Scene { Name = "Test" };
        var entity = scene.CreateEntity("Player");

        Assert.Single(scene.RootEntities);
        Assert.Equal("Player", entity.Name);
        Assert.Contains(entity, scene.AllEntities);
    }

    [Fact]
    public void Scene_EntityParentChild()
    {
        var scene = new Scene { Name = "Test" };
        var parent = scene.CreateEntity("Parent");
        var child = parent.AddChild("Child");

        Assert.Single(parent.Children);
        Assert.Equal(parent, child.Parent);
        Assert.Contains(child, scene.AllEntities);
    }

    [Fact]
    public void Scene_DestroyEntity()
    {
        var scene = new Scene { Name = "Test" };
        var entity = scene.CreateEntity("ToDestroy");
        long id = entity.Id;

        scene.DestroyEntity(entity);

        Assert.Empty(scene.RootEntities);
        Assert.Null(scene.FindById(id));
    }

    [Fact]
    public void Scene_FindByName()
    {
        var scene = new Scene { Name = "Test" };
        scene.CreateEntity("Player");
        scene.CreateEntity("Enemy");

        var found = scene.FindByName("Enemy");
        Assert.NotNull(found);
        Assert.Equal("Enemy", found!.Name);
    }

    [Fact]
    public void Scene_GetEntitiesWithComponent()
    {
        var scene = new Scene { Name = "Test" };
        var e1 = scene.CreateEntity("WithCamera");
        e1.Camera = new CameraComponent { IsPrimary = true };

        var e2 = scene.CreateEntity("WithoutCamera");
        e2.MeshRenderer = new MeshRendererComponent { MeshName = "Cube" };

        var cameras = scene.GetEntitiesWithComponent<CameraComponent>().ToList();
        Assert.Single(cameras);
        Assert.Equal("WithCamera", cameras[0].Name);
    }
}

public class TransformTests
{
    [Fact]
    public void Transform_DefaultValues()
    {
        var t = new TransformComponent();
        Assert.Equal(Vector3.Zero, t.Position);
        Assert.Equal(Quaternion.Identity, t.Rotation);
        Assert.Equal(Vector3.One, t.Scale);
    }

    [Fact]
    public void Transform_LocalToWorldMatrix()
    {
        var t = new TransformComponent
        {
            Position = new Vector3(1, 2, 3)
        };

        var matrix = t.LocalToWorldMatrix;
        Assert.Equal(1.0f, matrix.M41);
        Assert.Equal(2.0f, matrix.M42);
        Assert.Equal(3.0f, matrix.M43);
    }

    [Fact]
    public void Transform_SetRotationFromEuler()
    {
        var t = new TransformComponent();
        t.SetRotationFromEuler(0, 90, 0);

        // Verify rotation was applied (quaternion is not identity)
        Assert.NotEqual(Quaternion.Identity, t.Rotation);

        // Verify the forward vector changed (pointing along X after 90 degree yaw)
        var forward = t.Forward;
        Assert.True(Math.Abs(forward.X - 1.0f) < 0.1f || Math.Abs(forward.X + 1.0f) < 0.1f,
            $"Forward.X should be near ±1 after 90° yaw, got {forward.X}");
    }

    [Fact]
    public void Transform_ResetAndCopy()
    {
        var t1 = new TransformComponent
        {
            Position = new Vector3(5, 10, 15),
            Scale = new Vector3(2, 2, 2)
        };
        t1.SetRotationFromEuler(15, 30, 45);

        var t2 = new TransformComponent();
        t2.CopyFrom(t1);

        Assert.Equal(t1.Position, t2.Position);
        Assert.Equal(t1.Rotation, t2.Rotation);
        Assert.Equal(t1.Scale, t2.Scale);

        t2.ResetPosition();
        Assert.Equal(Vector3.Zero, t2.Position);

        t2.ResetRotation();
        Assert.Equal(Quaternion.Identity, t2.Rotation);

        t2.ResetScale();
        Assert.Equal(Vector3.One, t2.Scale);

        t1.ResetAll();
        Assert.Equal(Vector3.Zero, t1.Position);
        Assert.Equal(Quaternion.Identity, t1.Rotation);
        Assert.Equal(Vector3.One, t1.Scale);
    }
}

public class CameraTests
{
    [Fact]
    public void Camera_PerspectiveMatrix()
    {
        var cam = new Camera
        {
            Type = CameraType.Perspective,
            FieldOfView = 60,
            AspectRatio = 16.0f / 9.0f,
            NearPlane = 0.1f,
            FarPlane = 1000.0f
        };

        var proj = cam.GetProjectionMatrix();
        Assert.NotEqual(Matrix4x4.Identity, proj);
    }

    [Fact]
    public void Camera_ViewMatrix()
    {
        var cam = new Camera
        {
            Position = new Vector3(0, 0, 5),
            Target = Vector3.Zero
        };

        var view = cam.GetViewMatrix();
        Assert.NotEqual(Matrix4x4.Identity, view);
    }

    [Fact]
    public void Camera_Zoom()
    {
        var cam = new Camera
        {
            Position = new Vector3(0, 0, 5),
            Target = Vector3.Zero
        };

        float initialDistance = Vector3.Distance(cam.Position, cam.Target);
        cam.Zoom(-1.0f);
        float afterZoom = Vector3.Distance(cam.Position, cam.Target);

        Assert.True(afterZoom > initialDistance);
    }

    [Fact]
    public void Camera_Orbit_ZeroDelta_DoesNotFlipPosition()
    {
        var cam = new Camera
        {
            Position = new Vector3(0, 0, 3),
            Target = Vector3.Zero
        };

        cam.Orbit(0, 0);
        Assert.True(Math.Abs(cam.Position.Z - 3.0f) < 0.05f, $"Expected Z near 3.0, got {cam.Position.Z}");
        Assert.True(Math.Abs(cam.Position.X) < 0.05f, $"Expected X near 0, got {cam.Position.X}");
    }

    [Fact]
    public void Camera_FocusAndReset()
    {
        var cam = new Camera
        {
            Position = new Vector3(10, 10, 10),
            Target = Vector3.Zero
        };

        cam.Focus(new Vector3(5, 5, 5), 4.0f);
        Assert.Equal(new Vector3(5, 5, 5), cam.Target);
        Assert.True(Math.Abs(Vector3.Distance(cam.Position, cam.Target) - 4.0f) < 0.01f);

        cam.Reset();
        Assert.Equal(Vector3.Zero, cam.Target);
        Assert.Equal(new Vector3(0, 0, 3), cam.Position);
    }
}

public class SerializationTests
{
    [Fact]
    public void Scene_SerializeRoundTrip()
    {
        var scene = new Scene { Name = "Test Scene" };
        var entity = scene.CreateEntity("TestEntity");
        entity.Transform.Position = new Vector3(1, 2, 3);
        entity.MeshRenderer = new MeshRendererComponent { MeshName = "Cube" };

        string json = scene.Serialize();
        Assert.Contains("Test Scene", json);
        Assert.Contains("TestEntity", json);
    }
}

public class MathHelperTests
{
    [Fact]
    public void DegreesToRadians_Conversion()
    {
        float radians = MathHelper.DegreesToRadians(180);
        Assert.True(Math.Abs(radians - MathF.PI) < 0.001f);
    }

    [Fact]
    public void RadiansToDegrees_Conversion()
    {
        float degrees = MathHelper.RadiansToDegrees(MathF.PI);
        Assert.True(Math.Abs(degrees - 180.0f) < 0.001f);
    }
}
