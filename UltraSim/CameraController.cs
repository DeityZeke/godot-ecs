
using System;

using Godot;

/// <summary>
/// Simple camera controller for viewing ECS entities.
/// Attach to a Camera3D node.
/// 
/// Controls:
/// - WASD: Move camera
/// - Mouse Right-Click + Drag: Rotate camera
/// - Scroll Wheel: Zoom in/out
/// - Q/E: Move up/down
/// </summary>
public partial class CameraController : Camera3D
{
    [Export] public float MoveSpeed = 20f;
    [Export] public float MouseSensitivity = 0.1f;
    [Export] public float ZoomSpeed = 2f;
    [Export] public Vector3 InitialPosition = new Vector3(0, 50, 100);
    [Export] public Vector3 InitialRotation = new Vector3(-25, 0, 0);
    
    private bool rotating = false;
    private Vector3 cameraRotation;
    
    public override void _Ready()
    {
        // Set initial position and rotation
        Position = InitialPosition;
        RotationDegrees = InitialRotation;
        cameraRotation = RotationDegrees;
        
        // Make this the current camera
        MakeCurrent();
        
    }
    
    public override void _Input(InputEvent @event)
    {
        // Mouse rotation
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                rotating = mouseButton.Pressed;
                if (rotating)
                {
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                }
                else
                {
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                }
            }
            
            // Zoom with scroll wheel
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                Position += -Transform.Basis.Z * ZoomSpeed;
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                Position += Transform.Basis.Z * ZoomSpeed;
            }
        }
        
        if (@event is InputEventMouseMotion mouseMotion && rotating)
        {
            cameraRotation.Y -= mouseMotion.Relative.X * MouseSensitivity;
            cameraRotation.X -= mouseMotion.Relative.Y * MouseSensitivity;
            cameraRotation.X = Mathf.Clamp(cameraRotation.X, -89, 89);
            RotationDegrees = cameraRotation;
        }
    }
    
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Vector3.Zero;
        
        // Movement
        if (Input.IsKeyPressed(Key.W))
            velocity -= Transform.Basis.Z;
        if (Input.IsKeyPressed(Key.S))
            velocity += Transform.Basis.Z;
        if (Input.IsKeyPressed(Key.A))
            velocity -= Transform.Basis.X;
        if (Input.IsKeyPressed(Key.D))
            velocity += Transform.Basis.X;
        if (Input.IsKeyPressed(Key.Q))
            velocity.Y -= 1;
        if (Input.IsKeyPressed(Key.E))
            velocity.Y += 1;
        
        if (velocity.LengthSquared() > 0)
        {
            velocity = velocity.Normalized();
            Position += velocity * MoveSpeed * dt;
        }
        
        // Reset camera position with R key
        if (Input.IsKeyPressed(Key.R))
        {
            Position = InitialPosition;
            cameraRotation = InitialRotation;
            RotationDegrees = cameraRotation;
        }
    }
    
    /// <summary>
    /// Centers the camera on a specific point and orbits around it.
    /// </summary>
    public void LookAtPoint(Vector3 target, float distance = 50f)
    {
        Vector3 direction = (Position - target).Normalized();
        Position = target + direction * distance;
        LookAt(target);
        cameraRotation = RotationDegrees;
    }
}
