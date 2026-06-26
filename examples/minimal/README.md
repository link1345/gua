# Minimal Example

The smallest Gua loop is:

1. Create a runtime context.
2. Begin a frame.
3. Register a `Start Game` button.
4. Query it with a testing helper.
5. Enqueue and poll a click event.

```cpp
gua::Context context;
context.begin_frame("title");
context.button("start", "Start Game", { 100.0F, 200.0F, 240.0F, 64.0F });
context.end_frame();

gua::testing::get_by_role(context.native_handle(), "button", "Start Game").click();

gua::Event event;
while (context.poll_event(event)) {
    // event.node_id == "start"
}
```
