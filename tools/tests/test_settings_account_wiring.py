"""Static scene-contract checks, not Unity Play Mode tests.

Run with: python -m unittest discover -s tools/tests -v
Requires PyYAML: python -m pip install PyYAML
"""
from pathlib import Path
import re
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
SCENE = ROOT / "Assets/_Scenes/MainMenu.unity"
BUTTON = "4e29b1a8efbd4b44bb3f3716e73f07ff"
INPUT = "2da0c512f12947e489f739169773d7ca"
ACCOUNT = "42482d3606344878abbb1a228b23be5d"
SETTINGS = "7964339a950c4e5190a8ef3449d79270"
LOGIN = "aad2d2942e384c54bb0f9c4681f0a27b"


class AccountWiringTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        text = SCENE.read_text()
        cls.records = [
            (int(m[2]), next(iter(yaml.safe_load(m[3]).items())))
            for m in re.finditer(
                r"^--- !u!(\d+) &(\d+)\n(.*?)(?=^--- !u!|\Z)", text, re.M | re.S
            )
        ]
        cls.objects = dict(cls.records)

    def components(self, guid):
        return [(id, d) for id, (_, d) in self.objects.items()
                if d.get("m_Script", {}).get("guid") == guid]

    def account(self):
        matches = self.components(ACCOUNT)
        self.assertEqual(len(matches), 1)
        return matches[0]

    def test_unique_scene_ids(self):
        self.assertEqual(len(self.records), len(self.objects))

    def test_sign_out_is_a_button_with_one_direct_logout_call(self):
        go, obj = next((id, d) for id, (kind, d) in self.objects.items()
                       if kind == "GameObject" and d.get("m_Name") == "SIgn OUt Button")
        buttons = [d for _, d in self.components(BUTTON) if d["m_GameObject"]["fileID"] == go]
        self.assertEqual(len(buttons), 1)
        calls = buttons[0]["m_OnClick"]["m_PersistentCalls"]["m_Calls"]
        self.assertEqual(len(calls), 1)
        call = calls[0]
        login_id, _ = self.components(LOGIN)[0]
        self.assertEqual(call["m_Target"]["fileID"], login_id)
        self.assertEqual(call["m_TargetAssemblyTypeName"], "LoginPanel, Assembly-CSharp")
        self.assertEqual(call["m_MethodName"], "Logout")
        self.assertEqual(call["m_Mode"], 1)  # Void method.
        self.assertEqual(call["m_CallState"], 2)  # Runtime only.
        self.assertIn({"component": {"fileID": next(
            id for id, d in self.components(BUTTON) if d is buttons[0]
        )}}, obj["m_Component"])

    def test_settings_targets_real_account_component(self):
        id, _ = self.account()
        _, settings = self.components(SETTINGS)[0]
        self.assertEqual(settings["accountSettingsPanel"]["fileID"], id)

    def test_account_controls_have_correct_types(self):
        _, account = self.account()
        for field, guid in [("confirmEmailField", INPUT),
                            ("deleteButton", BUTTON), ("cancelButton", BUTTON)]:
            _, target = self.objects[account[field]["fileID"]]
            self.assertEqual(target["m_Script"]["guid"], guid)
        status = account["statusText"]["fileID"]
        self.assertIn(status, self.objects)

    def test_modal_starts_hidden_and_cancel_hides_entire_modal(self):
        _, account = self.account()
        root = account["m_GameObject"]["fileID"]
        self.assertEqual(account["panelRoot"]["fileID"], root)
        self.assertEqual(self.objects[root][1]["m_IsActive"], 0)
        self.assertEqual(self.objects[root][1]["m_Name"], "AccountSettingsPanel")
        # Reuse the object/component supplied in the user's scene.
        self.assertEqual(root, 632379966)
        self.assertEqual(self.account()[0], 632379968)

    def test_modal_is_last_canvas_child_not_child_of_settings(self):
        _, account = self.account()
        root = self.objects[account["m_GameObject"]["fileID"]][1]
        rect = root["m_Component"][0]["component"]["fileID"]
        parent = self.objects[rect][1]["m_Father"]["fileID"]
        canvas = self.objects[parent][1]
        self.assertEqual(self.objects[canvas["m_GameObject"]["fileID"]][1]["m_Name"], "Canvas")
        self.assertEqual(canvas["m_Children"][-1]["fileID"], rect)

    def test_account_button_events_not_double_wired(self):
        _, account = self.account()
        for field in ["deleteButton", "cancelButton"]:
            button = self.objects[account[field]["fileID"]][1]
            self.assertEqual(button["m_OnClick"]["m_PersistentCalls"]["m_Calls"], [])
        email = self.objects[account["confirmEmailField"]["fileID"]][1]
        self.assertEqual(email["m_OnSubmit"]["m_PersistentCalls"]["m_Calls"], [])

    def test_new_objects_have_no_dangling_local_references(self):
        def references(value):
            if isinstance(value, dict):
                if "fileID" in value and "guid" not in value:
                    yield value["fileID"]
                for child in value.values():
                    yield from references(child)
            elif isinstance(value, list):
                for child in value:
                    yield from references(child)
        for id, (_, obj) in self.objects.items():
            if id < 9000000000:
                continue
            for ref in references(obj):
                self.assertTrue(ref == 0 or ref in self.objects, (id, ref))
            if "m_Father" in obj:
                parent = obj["m_Father"]["fileID"]
                self.assertIn({"fileID": id}, self.objects[parent][1]["m_Children"])
            if "m_Component" in obj:
                for component in obj["m_Component"]:
                    target = self.objects[component["component"]["fileID"]][1]
                    self.assertEqual(target["m_GameObject"]["fileID"], id)

    def test_existing_api_flow_and_show_method_are_preserved(self):
        ui = ROOT / "Assets/_Scripts/UI"
        settings = (ui / "SettingsPanel.cs").read_text()
        account = (ui / "AccountSettingsPanel.cs").read_text()
        login = (ui / "LoginPanel.cs").read_text()
        self.assertIn("accountSettingsPanel.Show();", settings)
        self.assertIn("if (!gameObject.activeSelf) gameObject.SetActive(true);", account)
        self.assertIn("JadedBellesApiClient.Instance.RequestAccountDeletion(", account)
        self.assertIn("cancelButton.onClick.AddListener(OnCancelClicked)", account)
        self.assertIn("deleteButton.onClick.AddListener(OnDeleteClicked)", account)
        self.assertIn("JadedBellesApiClient.Instance.Logout(", login)
        self.assertNotIn("SessionService.MarkSignedOut()", settings)


if __name__ == "__main__":
    unittest.main()
