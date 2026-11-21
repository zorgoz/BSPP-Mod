using System;
using System.Collections.Generic;
using HMUI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProfilesMod
{
    internal sealed class ProfileDialogKeyboardGuard : MonoBehaviour
    {
        private InputFieldView _input;
        private UIKeyboardManager _manager;
        private readonly List<BaseRaycaster> _suspended = new List<BaseRaycaster>();
        private bool _warned;

        internal void Initialize(InputFieldView input, UIKeyboardManager manager)
        {
            _input = input;
            _manager = manager;
        }

        private void Update()
        {
            var selected = ProfileFlowCoordinator.ReadDependency<InputFieldView>(
                _manager, typeof(UIKeyboardManager), "_selectedInput");
            if (selected != _input || _input == null)
            {
                Restore();
                _warned = false;
                return;
            }
            if (_suspended.Count != 0)
                return;

            var keyboardRaycaster = _manager.keyboard == null ? null :
                _manager.keyboard.GetComponentInParent<BaseRaycaster>();
            var dialogRaycasters = GetComponents<BaseRaycaster>();
            if (keyboardRaycaster == null || !keyboardRaycaster.isActiveAndEnabled ||
                Array.IndexOf(dialogRaycasters, keyboardRaycaster) >= 0)
            {
                if (!_warned)
                {
                    Plugin.Log.Error("Cannot prevent playlist dialog keyboard overlap: the keyboard has no independent active raycaster.");
                    _warned = true;
                }
                return;
            }

            foreach (var raycaster in dialogRaycasters)
            {
                if (!raycaster.enabled)
                    continue;
                _suspended.Add(raycaster);
                raycaster.enabled = false;
            }
        }

        private void OnDisable()
        {
            Restore();
            _warned = false;
        }

        private void Restore()
        {
            foreach (var raycaster in _suspended)
                if (raycaster != null)
                    raycaster.enabled = true;
            _suspended.Clear();
        }
    }
}
