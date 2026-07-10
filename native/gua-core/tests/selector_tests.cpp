#include "gua/gua.h"
#include "gua/testing.hpp"

#include <cassert>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>

namespace {

void node(gua_context_t* context, const char* id, const char* parent, const char* role, const char* label, const char* text, bool visible = true)
{
    const gua_node_descriptor_v2_t descriptor {
        sizeof(gua_node_descriptor_v2_t),
        static_cast<uint64_t>((parent ? GUA_NODE_KNOWN_PARENT_ID : 0) | (text ? GUA_NODE_KNOWN_TEXT : 0)),
        id, parent, role, label, text, nullptr, { 0, 0, 1, 1 }, visible ? 1 : 0, 1, 0, 0, 0, 0, 0,
    };
    assert(gua_register_node_v2(context, &descriptor) == 1);
}

}

int main()
{
    gua_context_t* context = gua_create_context();
    gua_begin_frame(context, "selectors");
    node(context, "left", nullptr, "panel", "Left", nullptr);
    node(context, "right", nullptr, "panel", "Right", nullptr);
    node(context, "left-group", "left", "panel", "Group", nullptr);
    node(context, "left-save", "left-group", "button", "保存", "保存");
    node(context, "right-save", "right", "button", "保存", "保存");
    node(context, "hidden", "left", "button", "Hidden", "Hidden", false);
    const gua_node_descriptor_v2_t status {
        sizeof(gua_node_descriptor_v2_t), GUA_NODE_KNOWN_TEXT | GUA_NODE_KNOWN_VALUE,
        "status", nullptr, "status", "Status", "Ready", "2", { 0, 0, 1, 1 }, 1, 1, 0, 0, 0, 0, 0,
    };
    assert(gua_register_node_v2(context, &status) == 1);
    gua_end_frame(context);

    using namespace gua::testing;
    assert(query(context).by_role("button").query_all().size() == 3);
    assert(query(context).by_text("保存").within("left").get().id() == "left-save");
    assert(query(context).by_text("存", match_mode::contains).within("right", true).get().id() == "right-save");
    assert(query(context).by_name("^保.*$", match_mode::regex).within("left").where_visible().get().id() == "left-save");
    query(context).by_role("button").within("left", true).assert_count(1);

    bool ambiguous = false;
    try { (void)get_by_text(context, "保存"); }
    catch (const std::runtime_error& error) { ambiguous = std::string(error.what()).find("multiple") != std::string::npos; }
    assert(ambiguous);

    bool invalid_regex = false;
    try { (void)query(context).by_text("[", match_mode::regex).query_all(); }
    catch (const std::runtime_error& error) { invalid_regex = std::string(error.what()).find("Invalid Gua selector") != std::string::npos; }
    assert(invalid_regex);

    char legacy[128] {};
    assert(gua_find_node_by_text(context, "保存", legacy, sizeof(legacy)) == 1);
    assert(std::string(legacy) == "left-save");

    Locator status_locator(context, "status");
    status_locator.wait_for_visible(std::chrono::milliseconds(20), std::chrono::milliseconds(1));
    status_locator.wait_for_enabled(std::chrono::milliseconds(20), std::chrono::milliseconds(1));
    status_locator.wait_for_text("Ready", std::chrono::milliseconds(20), std::chrono::milliseconds(1));
    status_locator.wait_for_value("2", std::chrono::milliseconds(20), std::chrono::milliseconds(1));
    Locator(context, "missing").wait_for_hidden(std::chrono::milliseconds(20), std::chrono::milliseconds(1));

    bool stale_snapshot_rejected = false;
    try { wait_for_stable_snapshot(context, 3, std::chrono::milliseconds(5), std::chrono::milliseconds(1)); }
    catch (const std::runtime_error& error) {
        const std::string message(error.what());
        stale_snapshot_rejected = message.find("distinct stable semantic frames") != std::string::npos &&
            message.find("frameSequence=1") != std::string::npos && message.find("revision=1") != std::string::npos;
    }
    assert(stale_snapshot_rejected);

    const auto diagnostics_root = std::filesystem::temp_directory_path() / "gua-native-diagnostics-test";
    std::filesystem::remove_all(diagnostics_root);
    bool native_artifact_reported = false;
    {
        DiagnosticScope diagnostics({ diagnostics_root, "hidden assertion" });
        try { Locator(context, "hidden").to_be_visible(); }
        catch (const std::runtime_error& error) {
            native_artifact_reported = std::string(error.what()).find("Gua diagnostics:") != std::string::npos;
        }
    }
    assert(native_artifact_reported);
    bool native_artifact_exists = false;
    for (const auto& entry : std::filesystem::recursive_directory_iterator(diagnostics_root)) {
        if (entry.path().filename() == "diagnostics.json") native_artifact_exists = true;
    }
    assert(native_artifact_exists);
    const auto blocking_file = diagnostics_root / "not-a-directory";
    std::ofstream(blocking_file) << "block";
    bool native_capture_error_preserved = false;
    {
        DiagnosticScope diagnostics({ blocking_file, "writer failure" });
        try { Locator(context, "hidden").to_be_visible(); }
        catch (const std::runtime_error& error) {
            const std::string message(error.what());
            native_capture_error_preserved = message.starts_with("Expected Gua node") &&
                message.find("Gua diagnostics capture error:") != std::string::npos;
        }
    }
    assert(native_capture_error_preserved);
    std::filesystem::remove_all(diagnostics_root);

    int attempts = 0;
    wait_until([&attempts] { return ++attempts == 3; }, "third predicate evaluation", std::chrono::milliseconds(20), std::chrono::milliseconds(1));
    gua_destroy_context(context);
}
