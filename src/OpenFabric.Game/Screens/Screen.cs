using Microsoft.Xna.Framework;

namespace OpenFabric.Game.Screens;

public abstract class Screen
{
    protected readonly OpenFabricGame Host;
    protected Screen(OpenFabricGame host) { Host = host; }
    public virtual void Update(GameTime gt, float dt) { }
    /// <summary>Called with no SpriteBatch open; the screen manages its own batches.</summary>
    public abstract void Draw(GameTime gt);
    public virtual void Unload() { }
}
