﻿#region Copyright & License Information
/*
 * Copyright 2015- OpenRA.Mods.AS Developers (see AUTHORS)
 * This file is a part of a third-party plugin for OpenRA, which is
 * free software. It is made available to you under the terms of the
 * GNU General Public License as published by the Free Software
 * Foundation. For more information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.AS.Projectiles
{
	public class ParaBombInfo : IProjectileInfo
	{
		public readonly string Image = null;

		[Desc("Loop a randomly chosen sequence of Image from this list while falling.")]
		[SequenceReference("Image")]
		public readonly string[] Sequences = { "idle" };

		[Desc("Sequence to play when launched. Skipped if null or empty.")]
		[SequenceReference("Image")]
		public readonly string OpenSequence = null;

		[Desc("The palette used to draw this projectile.")]
		[PaletteReference]
		public readonly string Palette = "effect";

		[Desc("Palette is a player palette BaseName")]
		public readonly bool IsPlayerPalette = false;

		[Desc("Parachute opening sequence.")]
		[SequenceReference("Image")]
		public readonly string ParachuteOpeningSequence = null;

		[Desc("Parachute idle sequence.")]
		[SequenceReference("Image")]
		public readonly string ParachuteSequence = null;

		[Desc("Parachute closing sequence. Defaults to opening sequence played backwards.")]
		[SequenceReference("Image")]
		public readonly string ParachuteClosingSequence = null;

		[Desc("Palette used to render the parachute.")]
		[PaletteReference("ParachuteIsPlayerPalette")]
		public readonly string ParachutePalette = "player";
		public readonly bool ParachuteIsPlayerPalette = true;

		[Desc("Parachute position relative to the paradropped unit.")]
		public readonly WVec ParachuteOffset = new WVec(0, 0, 0);

		public readonly bool Shadow = false;

		[PaletteReference]
		public readonly string ShadowPalette = "shadow";

		[Desc("Projectile movement vector per tick (forward, right, up), use negative values for opposite directions.")]
		public readonly WVec Velocity = WVec.Zero;

		[Desc("Value added to Velocity every tick.")]
		public readonly WVec Acceleration = new WVec(0, 0, -15);

		[Desc("Type defined for point-defense logic.")]
		public readonly string PointDefenseType = null;

		public IProjectile Create(ProjectileArgs args) { return new ParaBomb(this, args); }
	}

	public class ParaBomb : IProjectile, ISync
	{
		readonly ParaBombInfo info;
		readonly Animation anim, parachute;
		readonly ProjectileArgs args;
		readonly WVec acceleration;
		[Sync]
		WVec velocity;
		[Sync]
		WPos pos;
		bool exploded;

		public ParaBomb(ParaBombInfo info, ProjectileArgs args)
		{
			this.info = info;
			this.args = args;
			pos = args.Source;
			var convertedVelocity = new WVec(info.Velocity.Y, -info.Velocity.X, info.Velocity.Z);
			velocity = convertedVelocity.Rotate(WRot.FromFacing(args.Facing));
			acceleration = new WVec(info.Acceleration.Y, -info.Acceleration.X, info.Acceleration.Z);

			if (!string.IsNullOrEmpty(info.Image))
			{
				anim = new Animation(args.SourceActor.World, info.Image, () => args.Facing);

				if (!string.IsNullOrEmpty(info.OpenSequence))
					anim.PlayThen(info.OpenSequence, () => anim.PlayRepeating(info.Sequences.Random(args.SourceActor.World.SharedRandom)));
				else
					anim.PlayRepeating(info.Sequences.Random(args.SourceActor.World.SharedRandom));

				parachute = new Animation(args.SourceActor.World, info.Image, () => args.Facing);
				parachute.PlayThen(info.ParachuteOpeningSequence, () => parachute.PlayRepeating(info.ParachuteSequence));
			}
		}

		public void Tick(World world)
		{
			if (!exploded)
			{
				pos += velocity;
				velocity += acceleration;

				if (pos.Z <= args.PassiveTarget.Z)
				{
					pos += new WVec(0, 0, args.PassiveTarget.Z - pos.Z);
					args.Weapon.Impact(Target.FromPos(pos), new WarheadArgs(args));
					exploded = true;

					if (!string.IsNullOrEmpty(info.ParachuteClosingSequence))
						parachute.PlayThen(info.ParachuteClosingSequence, () => world.AddFrameEndTask(w => w.Remove(this)));
					else
						parachute.PlayBackwardsThen(info.ParachuteOpeningSequence, () => world.AddFrameEndTask(w => w.Remove(this)));
				}

				if (!string.IsNullOrEmpty(info.PointDefenseType))
				{
					var shouldExplode = world.ActorsWithTrait<IPointDefense>().Any(x => x.Trait.Destroy(pos, args.SourceActor.Owner, info.PointDefenseType));
					if (shouldExplode)
					{
						args.Weapon.Impact(Target.FromPos(pos), new WarheadArgs(args));
						exploded = true;
						world.AddFrameEndTask(w => w.Remove(this));
					}
				}

				if (anim != null)
					anim.Tick();
			}

			if (parachute != null)
				parachute.Tick();
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			if (anim == null && parachute == null)
				yield break;

			var world = args.SourceActor.World;
			if (!world.FogObscures(pos))
			{
				if (!exploded)
				{
					if (info.Shadow)
					{
						var dat = world.Map.DistanceAboveTerrain(pos);
						var shadowPos = pos - new WVec(0, 0, dat.Length);
						foreach (var r in anim.Render(shadowPos, wr.Palette(info.ShadowPalette)))
							yield return r;
					}

					var palette = wr.Palette(info.Palette + (info.IsPlayerPalette ? args.SourceActor.Owner.InternalName : ""));
					foreach (var r in anim.Render(pos, palette))
						yield return r;
				}

				var chutepalette = wr.Palette(info.ParachutePalette + (info.ParachuteIsPlayerPalette ? args.SourceActor.Owner.InternalName : ""));
				foreach (var r in parachute.Render(pos + info.ParachuteOffset, chutepalette))
					yield return r;
			}
		}
	}
}
