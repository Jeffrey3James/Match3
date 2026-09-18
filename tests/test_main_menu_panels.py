"""Static scene/lifecycle regression checks.

Run: python -m unittest discover -s tests -v
Dependency: PyYAML (python -m pip install PyYAML).
These checks do not replace Unity compilation or Play Mode testing.
"""
import re
import unittest
from pathlib import Path

import yaml


ROOT = Path(__file__).resolve().parents[1]
SCENE = ROOT / "Assets/_Scenes/MainMenu.unity"
UI = ROOT / "Assets/_Scripts/UI"
BUTTON_GUID = "4e29b1a8efbd4b44bb3f3716e73f07ff"
IMAGE_GUID = "fe87c0e1cc204ed48ad3b37840f39efc"
CLOSE_SPRITE_GUID = "d28e01947f8872e4a955f7757ab43b7f"
PANELS = {
    "ShopPanel": (261378739, 261378743),
    "SettingsPanel": (2055801334, 2055801336),
    "LoginPanel": (1464348017, 1464348019),
    "PreLevelBoosterPanel": (1036967282, 1036967284),
}


def method_body(source, name):
    match = re.search(r"\b" + name + r"\s*\([^)]*\)\s*\{", source)
    if not match:
        raise AssertionError(f"Missing method {name}")
    start = match.end()
    depth = 1
    for pos in range(start, len(source)):
        depth += (source[pos] == "{") - (source[pos] == "}")
        if depth == 0:
            return source[start:pos]
    raise AssertionError(f"Unclosed method {name}")


class MainMenuPanelTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.text = SCENE.read_text()
        cls.blocks = re.findall(
            r"^--- !u!\d+ &(\d+)\n(.*?)(?=^---|\Z)",
            cls.text, re.MULTILINE | re.DOTALL,
        )
        cls.docs = {int(i): yaml.safe_load(body) for i, body in cls.blocks}
        cls.objects = {i: next(iter(doc.values())) for i, doc in cls.docs.items()}
        cls.sources = {name: (UI / f"{name}.cs").read_text() for name in PANELS}

    def transform(self, game_object):
        for item in self.objects[game_object]["m_Component"]:
            component = item["component"]["fileID"]
            if "RectTransform" in self.docs[component]:
                return component, self.objects[component]
        self.fail(f"No RectTransform on {game_object}")

    def test_no_duplicate_scene_ids(self):
        self.assertEqual(len(self.blocks), len(self.docs))

    def test_scene_local_references_resolve(self):
        for match in re.finditer(r"\{fileID: (-?\d+)\}", self.text):
            file_id = int(match[1])
            if file_id:
                self.assertIn(file_id, self.docs)

    def test_transform_parents_and_children_agree(self):
        for file_id, doc in self.docs.items():
            if "RectTransform" not in doc and "Transform" not in doc:
                continue
            obj = self.objects[file_id]
            for child in obj["m_Children"]:
                self.assertEqual(self.objects[child["fileID"]]["m_Father"]["fileID"], file_id)
            parent = obj["m_Father"]["fileID"]
            if parent:
                self.assertIn({"fileID": file_id}, self.objects[parent]["m_Children"])

    def test_popups_start_hidden_and_keep_inner_content_active(self):
        for name, (root, _) in PANELS.items():
            with self.subTest(panel=name):
                self.assertEqual(self.objects[root]["m_IsActive"], 0)
                _, transform = self.transform(root)
                for child in transform["m_Children"]:
                    child_go = self.objects[child["fileID"]]["m_GameObject"]["fileID"]
                    self.assertEqual(self.objects[child_go]["m_IsActive"], 1)

    def test_each_popup_has_one_wired_clickable_x(self):
        for name, (root, controller_id) in PANELS.items():
            with self.subTest(panel=name):
                controller = self.objects[controller_id]
                button = self.objects[controller["closeButton"]["fileID"]]
                self.assertEqual(button["m_Script"]["guid"], BUTTON_GUID)
                self.assertEqual(button["m_Interactable"], 1)
                self.assertEqual(button["m_Enabled"], 1)
                self.assertEqual(button["m_OnClick"]["m_PersistentCalls"]["m_Calls"], [])
                button_go = button["m_GameObject"]["fileID"]
                self.assertIn({"component": controller["closeButton"]},
                              self.objects[button_go]["m_Component"])
                image = self.objects[button["m_TargetGraphic"]["fileID"]]
                self.assertEqual(image["m_Script"]["guid"], IMAGE_GUID)
                self.assertEqual(image["m_Sprite"]["guid"], CLOSE_SPRITE_GUID)
                self.assertEqual(image["m_RaycastTarget"], 1)
                self.assertEqual(image["m_GameObject"]["fileID"], button_go)
                transform_id, transform = self.transform(button_go)
                self.assertEqual(transform["m_AnchorMin"], {"x": 1, "y": 1})
                self.assertEqual(transform["m_AnchorMax"], {"x": 1, "y": 1})
                self.assertGreaterEqual(transform["m_SizeDelta"]["x"], 100)
                self.assertGreaterEqual(transform["m_SizeDelta"]["y"], 100)
                self.assertLess(transform["m_AnchoredPosition"]["x"], 0)
                self.assertLess(transform["m_AnchoredPosition"]["y"], 0)
                parent = transform["m_Father"]["fileID"]
                self.assertEqual(self.objects[parent]["m_Children"][-1]["fileID"], transform_id)
                ancestors = []
                while parent:
                    ancestors.append(self.objects[parent]["m_GameObject"]["fileID"])
                    parent = self.objects[parent]["m_Father"]["fileID"]
                self.assertIn(root, ancestors)

    def test_close_root_includes_frame_and_raycast_blocker(self):
        for name, (root, controller_id) in PANELS.items():
            with self.subTest(panel=name):
                if name != "ShopPanel":  # ShopPanel hides its own GameObject.
                    self.assertEqual(self.objects[controller_id]["panelRoot"]["fileID"], root)

    def test_all_panel_controllers_expose_show_and_hide(self):
        for name, source in self.sources.items():
            with self.subTest(panel=name):
                self.assertRegex(source, r"public void Show\s*\(")
                self.assertRegex(source, r"public void Hide\s*\(")
                self.assertIn("SetActive(true)", method_body(source, "Show"))
                self.assertIn("SetActive(false)", method_body(source, "Hide"))

    def test_close_listeners_are_added_and_removed(self):
        for name, source in self.sources.items():
            with self.subTest(panel=name):
                self.assertRegex(source, r"closeButton\.onClick\.AddListener\(")
                self.assertRegex(source, r"closeButton\.onClick\.RemoveListener\(")

    def test_no_panel_hides_itself_from_start(self):
        for name, source in self.sources.items():
            with self.subTest(panel=name):
                if re.search(r"void Start\s*\(", source):
                    body = method_body(source, "Start")
                    if name == "LoginPanel":
                        self.assertEqual(self.objects[PANELS[name][1]]["resolveSessionOnStart"], 0)
                        self.assertIn("if (!resolveSessionOnStart) return;", body)
                        body = body.split("if (!resolveSessionOnStart) return;")[0]
                    self.assertNotIn("Hide();", body)
                    self.assertNotIn("SetActive(false)", body)

    def test_controller_guids_match_the_actual_scripts(self):
        for name, (_, controller_id) in PANELS.items():
            with self.subTest(panel=name):
                meta = yaml.safe_load((UI / f"{name}.cs.meta").read_text())
                self.assertEqual(self.objects[controller_id]["m_Script"]["guid"], meta["guid"])

    def test_navigation_has_all_popup_and_auth_gate_references(self):
        menu = self.objects[1425697071]
        self.assertEqual(menu["shopPanel"]["fileID"], PANELS["ShopPanel"][0])
        self.assertEqual(menu["settingsPanel"]["fileID"], PANELS["SettingsPanel"][0])
        self.assertEqual(menu["preLevelBoosterPanel"]["fileID"], PANELS["PreLevelBoosterPanel"][1])
        self.assertEqual(menu["loginPanel"]["fileID"], PANELS["LoginPanel"][1])
        self.assertEqual(menu["navigationRoot"]["fileID"], 1426579226)

    def test_navigation_uses_panel_lifecycle_and_preserves_auth_gate(self):
        source = (UI / "MainMenuUI.cs").read_text()
        home = method_body(source, "SetToHome")
        self.assertNotIn("loginPanel.Hide", home)
        self.assertNotIn("shopPanel != null && settingsPanel != null", home)
        self.assertIn("preLevelBoosterPanel.Hide()", home)
        for name in ("ShowShopPanel", "ShowSettingsPanel", "ShowPreLevelBoosterPanel"):
            with self.subTest(method=name):
                body = method_body(source, name)
                self.assertIn("!SessionService.IsResolved", body)
                self.assertIn("SetToHome();", body)
                self.assertIn(".Show();", body)
        self.assertIn("navigationRoot.SetActive(visible)", method_body(source, "SetMenuContentVisible"))

    def test_login_x_is_safe_for_busy_and_signed_in_sessions(self):
        body = method_body(self.sources["LoginPanel"], "OnCloseClicked")
        self.assertIn("if (_busy) return;", body)
        self.assertIn("if (SessionService.IsResolved) Hide();", body)
        self.assertIn("else OnGuestClicked();", body)
        self.assertNotIn("Logout", body)
        self.assertIn("closeButton.interactable = !busy",
                      method_body(self.sources["LoginPanel"], "SetBusy"))

    def test_booster_x_does_not_spend_or_start_a_level(self):
        source = self.sources["PreLevelBoosterPanel"]
        self.assertIn("closeButton.onClick.AddListener(Hide)", source)
        body = method_body(source, "Hide")
        for forbidden in ("SpendCoins", "LoadScene", "TryConsumeInventory", "OnPlayClicked"):
            self.assertNotIn(forbidden, body)


if __name__ == "__main__":
    unittest.main()
