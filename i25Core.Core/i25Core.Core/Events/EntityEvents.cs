using System.Text.Json.Serialization;

namespace i25Core.Core.Events
{
    /// <summary>
    /// Raised automatically after a BaseEntity is saved (Laravel: model events).
    /// Listen to <see cref="EntityEvent"/> to hear every entity change, to
    /// <c>EntityEvent&lt;Vendor&gt;</c> for every Vendor change, or to
    /// <c>EntityCreatedEvent&lt;Vendor&gt;</c> for one kind of change.
    /// </summary>
    public abstract class EntityEvent : IEvent
    {
        /// <summary>The saved entity as object (use the generic events for a typed value).</summary>
        [JsonIgnore]
        public abstract object EntityObject { get; }

        [JsonIgnore]
        public Type EntityType => EntityObject.GetType();
    }

    public abstract class EntityEvent<TEntity> : EntityEvent where TEntity : class
    {
        public TEntity Entity { get; set; } = default!;

        [JsonIgnore]
        public override object EntityObject => Entity;
    }

    /// <summary>A new row was inserted. <c>Entity.id</c> already holds the database-generated id.</summary>
    public class EntityCreatedEvent<TEntity> : EntityEvent<TEntity> where TEntity : class
    {
    }

    /// <summary>A row was changed. <see cref="ChangedProperties"/> lists the fields that changed.</summary>
    public class EntityUpdatedEvent<TEntity> : EntityEvent<TEntity> where TEntity : class
    {
        public List<string> ChangedProperties { get; set; } = new();
    }

    /// <summary>A row was deleted. The entity still holds its last values.</summary>
    public class EntityDeletedEvent<TEntity> : EntityEvent<TEntity> where TEntity : class
    {
    }
}
