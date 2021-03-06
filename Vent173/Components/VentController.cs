// -----------------------------------------------------------------------
// <copyright file="VentController.cs" company="Build">
// Copyright (c) Build. All rights reserved.
// Licensed under the CC BY-SA 3.0 license.
// </copyright>
// -----------------------------------------------------------------------

namespace Vent173.Components
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using CustomPlayerEffects;
    using Exiled.API.Enums;
    using Exiled.API.Extensions;
    using Exiled.API.Features;
    using Interactables.Interobjects.DoorUtils;
    using MEC;
    using UnityEngine;
    using Vent173.Managers;

    /// <summary>
    /// Manages the venting functionality for Scp173.
    /// </summary>
    public class VentController : MonoBehaviour
    {
        private Plugin plugin;
        private EventHandlers eventHandlers;
        private CoroutineHandle abilityCoroutine;
        private CoroutineHandle readyCoroutine;
        private float abilityCooldown;
        private float attackCooldown;

        /// <summary>
        /// Gets or sets a value indicating whether the current delay is considered to be the first of the round.
        /// </summary>
        public static bool InitialDelay { get; set; } = true;

        /// <summary>
        /// Gets the player this component is attached to.
        /// </summary>
        public Player Player { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the Player is venting.
        /// </summary>
        public bool IsVenting { get; private set; }

        /// <summary>
        /// Attempts to toggle the current venting status.
        /// </summary>
        /// <param name="response">The message to be relayed to the Player.</param>
        /// <returns>Whether the toggle was successful.</returns>
        public bool ToggleVent(out string response)
        {
            if (IsVenting)
            {
                StopVenting();
                response = plugin.Translation.Ending.Content;
                return true;
            }

            return TryVent(out response);
        }

        /// <summary>
        /// Attempts to destroy this component.
        /// </summary>
        public void Destroy()
        {
            try
            {
                Destroy(this);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to destroy {nameof(VentController)}: {e}");
            }
        }

        private static void DoBlink() => Scp173PlayerScript._remainingTime = 0f;

        private bool TryVent(out string response)
        {
            if (!plugin.Config.CanVentOnSurface && Player.Zone == ZoneType.Surface)
            {
                response = plugin.Translation.CannotVentOnSurface;
                return false;
            }

            if (InitialDelay)
            {
                response = plugin.Translation.OnCooldown.Replace("%seconds", (plugin.Config.InitialDelay - (int)Round.ElapsedTime.TotalSeconds).ToString(CultureInfo.CurrentCulture));
                return false;
            }

            if (abilityCooldown != 0f)
            {
                response = plugin.Translation.OnCooldown.Replace("%seconds", ((int)abilityCooldown).ToString());
                return false;
            }

            StartVenting();
            response = plugin.Translation.Activation.Content;
            return true;
        }

        private void StartVenting()
        {
            IsVenting = true;
            DoBlink();
            Timing.RunCoroutine(ToggleDoors(true));

            Player.EnableEffect<Amnesia>();
            var scp207 = Player.GetEffect(EffectType.Scp207);
            scp207.Intensity = 4;
            Player.EnableEffect(scp207);

            Player.Broadcast(plugin.Translation.Activation, true);

            abilityCoroutine = Timing.RunCoroutine(RunAbilityCoroutine());

            Player.SessionVariables.Add("IsVenting", true);
        }

        private void StopVenting()
        {
            IsVenting = false;
            DoBlink();
            Timing.RunCoroutine(ToggleDoors(false));

            Player.DisableEffect<Scp207>();
            Player.DisableEffect<Amnesia>();

            Timing.KillCoroutines(abilityCoroutine);
            readyCoroutine = Timing.RunCoroutine(RunReadyCoroutine());

            abilityCooldown = plugin.Config.Cooldown;
            attackCooldown = plugin.Config.KillCooldown;

            Player.SessionVariables.Remove("IsVenting");
        }

        private void Awake()
        {
            Player = Player.Get(gameObject);
            plugin = Plugin.Instance;
            eventHandlers = new EventHandlers(this);
            eventHandlers.SubscribeAll();
            Player.Broadcast(plugin.Translation.Spawn, true);
        }

        private void Update()
        {
            if (Player == null || Player.Role != RoleType.Scp173)
            {
                Destroy();
                return;
            }

            if (IsVenting && !plugin.Config.CanVentOnSurface && Player.Zone == ZoneType.Surface)
            {
                Timing.KillCoroutines(abilityCoroutine);
                StopVenting();
            }

            UpdateCooldowns();
        }

        private void OnDestroy()
        {
            Timing.KillCoroutines(abilityCoroutine, readyCoroutine);
            eventHandlers.UnsubscribeAll();
            eventHandlers = null;
            if (Player == null)
                return;

            StopVenting();
        }

        private void UpdateCooldowns()
        {
            if (abilityCooldown != 0f)
                abilityCooldown = Mathf.Clamp(abilityCooldown -= Time.deltaTime, 0f, plugin.Config.Cooldown);

            if (attackCooldown != 0f)
                attackCooldown = Mathf.Clamp(attackCooldown -= Time.deltaTime, 0f, plugin.Config.KillCooldown);
        }

        private IEnumerator<float> RunAbilityCoroutine()
        {
            yield return Timing.WaitForSeconds(plugin.Config.VentDuration);
            StopVenting();
        }

        private IEnumerator<float> RunReadyCoroutine()
        {
            yield return Timing.WaitForSeconds(plugin.Config.Cooldown);
            Player.Broadcast(plugin.Translation.Ready, true);
        }

        private IEnumerator<float> ToggleDoors(bool hide)
        {
            foreach (var door in Map.Doors)
            {
                var requiredPermissions = door.RequiredPermissions.RequiredPermissions;
                if (!plugin.Config.CanVentThroughLocks &&
                    requiredPermissions != KeycardPermissions.None &&
                    requiredPermissions != KeycardPermissions.Checkpoints)
                    continue;

                Player.SendFakeSyncVar(door.netIdentity, typeof(DoorVariant), nameof(DoorVariant.NetworkTargetState), !hide);
                yield return Timing.WaitForOneFrame;
            }
        }
    }
}