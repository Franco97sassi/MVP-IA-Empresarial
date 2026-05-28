import { describe, expect, it } from "vitest";
import { parseSseBuffer } from "./chatSseParser";

describe("parseSseBuffer", () => {
  it("parsea eventos completos y deja resto incompleto", () => {
    const input = "event: meta\ndata: {\"conversationId\":1}\n\nevent: chunk\ndata: hola\n\nevent: done\ndata: {}\n\nparcial";
    const { events, rest } = parseSseBuffer(input);

    expect(events).toHaveLength(3);
    expect(events[0]).toEqual({ event: "meta", data: '{"conversationId":1}' });
    expect(events[1]).toEqual({ event: "chunk", data: "hola" });
    expect(rest).toBe("parcial");
  });
});
