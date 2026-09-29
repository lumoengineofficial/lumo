using System.Numerics;

namespace Lumo.Engine.Scene;

/// <summary>
/// Handles serialization/deserialization between Scene objects and data format.
/// </summary>
public static class SceneSerializer
{
    public static SceneData Serialize(Scene scene)
    {
        var data = new SceneData
        {
            Name = scene.Name
        };

        foreach (var entity in scene.RootEntities)
        {
            SerializeEntity(entity, null, data.Entities);
        }

        return data;
    }

    private static void SerializeEntity(Entity entity, long? parentId, List<EntityData> list)
    {
        var entityData = new EntityData
        {
            Name = entity.Name,
            Id = entity.Id,
            ParentId = parentId,
            Transform = new TransformData
            {
                Position = [entity.Transform.Position.X, entity.Transform.Position.Y, entity.Transform.Position.Z],
                Rotation = [entity.Transform.Rotation.X, entity.Transform.Rotation.Y, entity.Transform.Rotation.Z, entity.Transform.Rotation.W],
                Scale = [entity.Transform.Scale.X, entity.Transform.Scale.Y, entity.Transform.Scale.Z]
            }
        };

        if (entity.MeshRenderer != null)
        {
            entityData.MeshRenderer = new MeshRendererData
            {
                MeshName = entity.MeshRenderer.MeshName,
                MaterialName = entity.MeshRenderer.MaterialName,
                IsVisible = entity.MeshRenderer.IsVisible,
                Color = [entity.MeshRenderer.Color.X, entity.MeshRenderer.Color.Y, entity.MeshRenderer.Color.Z]
            };
        }

        if (entity.SpriteRenderer != null)
        {
            entityData.SpriteRenderer = new SpriteRendererData
            {
                SpritePath = entity.SpriteRenderer.SpritePath,
                Width = entity.SpriteRenderer.Width,
                Height = entity.SpriteRenderer.Height,
                SortOrder = entity.SpriteRenderer.SortOrder,
                IsVisible = entity.SpriteRenderer.IsVisible,
                Color = [entity.SpriteRenderer.Color.X, entity.SpriteRenderer.Color.Y, entity.SpriteRenderer.Color.Z]
            };
        }

        if (entity.Camera != null)
        {
            entityData.Camera = new CameraData
            {
                IsPrimary = entity.Camera.IsPrimary,
                FieldOfView = entity.Camera.FieldOfView,
                NearPlane = entity.Camera.NearPlane,
                FarPlane = entity.Camera.FarPlane
            };
        }

        if (entity.Light != null)
        {
            entityData.Light = new LightData
            {
                LightType = entity.Light.LightType.ToString(),
                Intensity = entity.Light.Intensity,
                Color = [entity.Light.Color.X, entity.Light.Color.Y, entity.Light.Color.Z]
            };
        }

        if (entity.Scripts != null)
        {
            entityData.Scripts = new ScriptsData
            {
                Enabled = entity.Scripts.Enabled,
                ScriptNames = [.. entity.Scripts.ScriptNames]
            };
        }

        list.Add(entityData);

        foreach (var child in entity.Children)
        {
            SerializeEntity(child, entity.Id, list);
        }
    }

    public static Scene Deserialize(SceneData data)
    {
        var scene = new Scene { Name = data.Name };

        var entityMap = new Dictionary<long, Entity>();

        // First pass: create all entities
        foreach (var entityData in data.Entities)
        {
            var entity = new Entity(entityData.Name)
            {
                ParentScene = scene
            };
            entityMap[entityData.Id] = entity;

            if (entityData.Transform != null)
            {
                entity.Transform.Position = new Vector3(
                    entityData.Transform.Position[0],
                    entityData.Transform.Position[1],
                    entityData.Transform.Position[2]);

                if (entityData.Transform.Rotation.Length >= 4)
                {
                    entity.Transform.Rotation = new Quaternion(
                        entityData.Transform.Rotation[0],
                        entityData.Transform.Rotation[1],
                        entityData.Transform.Rotation[2],
                        entityData.Transform.Rotation[3]);
                }

                entity.Transform.Scale = new Vector3(
                    entityData.Transform.Scale[0],
                    entityData.Transform.Scale[1],
                    entityData.Transform.Scale[2]);
            }

            if (entityData.MeshRenderer != null)
            {
                entity.MeshRenderer = new MeshRendererComponent
                {
                    MeshName = entityData.MeshRenderer.MeshName,
                    MaterialName = entityData.MeshRenderer.MaterialName,
                    IsVisible = entityData.MeshRenderer.IsVisible,
                    Color = entityData.MeshRenderer.Color is { Length: >= 3 } c
                        ? new Vector3(c[0], c[1], c[2])
                        : new Vector3(0.55f, 0.62f, 0.75f)
                };
            }

            if (entityData.SpriteRenderer != null)
            {
                entity.SpriteRenderer = new SpriteRendererComponent
                {
                    SpritePath = entityData.SpriteRenderer.SpritePath,
                    Width = entityData.SpriteRenderer.Width,
                    Height = entityData.SpriteRenderer.Height,
                    SortOrder = entityData.SpriteRenderer.SortOrder,
                    IsVisible = entityData.SpriteRenderer.IsVisible,
                    Color = entityData.SpriteRenderer.Color.Length >= 3
                        ? new System.Numerics.Vector3(
                            entityData.SpriteRenderer.Color[0],
                            entityData.SpriteRenderer.Color[1],
                            entityData.SpriteRenderer.Color[2])
                        : new System.Numerics.Vector3(0.47f, 0.67f, 0.94f)
                };
            }

            if (entityData.Camera != null)
            {
                entity.Camera = new CameraComponent
                {
                    IsPrimary = entityData.Camera.IsPrimary,
                    FieldOfView = entityData.Camera.FieldOfView,
                    NearPlane = entityData.Camera.NearPlane,
                    FarPlane = entityData.Camera.FarPlane
                };
            }

            if (entityData.Light != null)
            {
                entity.Light = new LightComponent
                {
                    LightType = Enum.TryParse<Rendering.Abstractions.LightType>(entityData.Light.LightType, out var lt)
                        ? lt : Rendering.Abstractions.LightType.Directional,
                    Intensity = entityData.Light.Intensity,
                    Color = new Vector3(
                        entityData.Light.Color[0],
                        entityData.Light.Color[1],
                        entityData.Light.Color[2])
                };
            }

            if (entityData.Scripts != null)
            {
                var sc = new Lumo.Engine.Scripting.ScriptComponent
                {
                    Enabled = entityData.Scripts.Enabled
                };
                sc.ScriptNames.AddRange(entityData.Scripts.ScriptNames);
                entity.Scripts = sc;
            }
        }

        // Second pass: set up hierarchy
        foreach (var entityData in data.Entities)
        {
            if (entityData.ParentId.HasValue && entityMap.TryGetValue(entityData.ParentId.Value, out var parent))
            {
                var child = entityMap[entityData.Id];
                child.Parent = parent;
                parent.Children.Add(child);
            }
            else
            {
                scene.RootEntities.Add(entityMap[entityData.Id]);
            }
        }

        // Populate AllEntities — without this, loaded scenes appear empty.
        foreach (var entity in entityMap.Values)
            scene.RegisterLoadedEntity(entity);

        return scene;
    }
}
