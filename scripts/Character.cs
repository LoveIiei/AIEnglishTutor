// Character.cs
using Godot;
using System;

public partial class Character : Node2D
{
    private AnimatedSprite2D animatedSprite;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        // Get a reference to the animation node.
        // The generic <AnimatedSprite2D> ensures type safety.
        animatedSprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
    }

    public void PlayAnimation(string animationName)
    {
        if (animationName == "idle" || animationName == "talking" || animationName == "listening")
        {
            animatedSprite.Play(animationName);
        }
    }
}