import { render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SciFiBackgroundOidc } from "./sci-fi-background-oidc";

describe("SciFiBackgroundOidc", () => {
  it("renders the two background canvases", () => {
    const { container } = render(<SciFiBackgroundOidc />);
    expect(container.querySelectorAll("canvas")).toHaveLength(2);
  });

  it("renders the corner brackets by default", () => {
    const { container } = render(<SciFiBackgroundOidc />);
    expect(container.querySelector(".corner-tl")).not.toBeNull();
    expect(container.querySelector(".corner-br")).not.toBeNull();
  });

  it("omits the corner brackets when showCorners is false", () => {
    const { container } = render(<SciFiBackgroundOidc showCorners={false} />);
    expect(container.querySelector(".corner-tl")).toBeNull();
  });

  describe("with mocked canvas and animation frame", () => {
    const make2dCtx = () => ({
      setTransform: vi.fn(),
      clearRect: vi.fn(),
      fillRect: vi.fn(),
      createRadialGradient: vi.fn(() => ({ addColorStop: vi.fn() })),
      fillStyle: "",
    });

    const makeGl = () => ({
      VERTEX_SHADER: 1,
      FRAGMENT_SHADER: 2,
      ARRAY_BUFFER: 3,
      STATIC_DRAW: 4,
      FLOAT: 5,
      TRIANGLES: 6,
      viewport: vi.fn(),
      createShader: vi.fn(() => ({})),
      shaderSource: vi.fn(),
      compileShader: vi.fn(),
      createProgram: vi.fn(() => ({})),
      attachShader: vi.fn(),
      linkProgram: vi.fn(),
      useProgram: vi.fn(),
      createBuffer: vi.fn(() => ({})),
      bindBuffer: vi.fn(),
      bufferData: vi.fn(),
      getAttribLocation: vi.fn(() => 0),
      enableVertexAttribArray: vi.fn(),
      vertexAttribPointer: vi.fn(),
      getUniformLocation: vi.fn(() => ({})),
      uniform1f: vi.fn(),
      uniform2f: vi.fn(),
      drawArrays: vi.fn(),
    });

    beforeEach(() => {
      // Run each top-level frame callback exactly once. A depth guard stops the
      // callback's own requestAnimationFrame call from recursing forever.
      let depth = 0;
      vi.spyOn(window, "requestAnimationFrame").mockImplementation(
        (cb: FrameRequestCallback) => {
          if (depth === 0) {
            depth++;
            cb(0);
            depth--;
          }
          return 1;
        },
      );
      vi.spyOn(window, "cancelAnimationFrame").mockImplementation(() => {});
    });

    afterEach(() => {
      vi.restoreAllMocks();
    });

    it("runs the 2-D atmospheric draw loop when a 2d context is available", () => {
      const ctx = make2dCtx();
      vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockImplementation(
        ((type: string) =>
          type === "2d"
            ? (ctx as unknown as CanvasRenderingContext2D)
            : null) as typeof HTMLCanvasElement.prototype.getContext,
      );

      render(<SciFiBackgroundOidc />);
      expect(ctx.setTransform).toHaveBeenCalled();
      expect(ctx.clearRect).toHaveBeenCalled();
      expect(ctx.createRadialGradient).toHaveBeenCalled();
    });

    it("runs the WebGL plasma effect when a webgl context is available", () => {
      const ctx2d = make2dCtx();
      const gl = makeGl();
      vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockImplementation(
        ((type: string) => {
          if (type === "2d") return ctx2d as unknown as CanvasRenderingContext2D;
          if (type === "webgl") return gl as unknown as WebGLRenderingContext;
          return null;
        }) as typeof HTMLCanvasElement.prototype.getContext,
      );

      render(<SciFiBackgroundOidc />);
      expect(gl.linkProgram).toHaveBeenCalled();
      expect(gl.drawArrays).toHaveBeenCalled();
    });

    it("skips the WebGL effect when the opacity variable is zero", () => {
      const gl = makeGl();
      vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockImplementation(
        ((type: string) =>
          type === "webgl"
            ? (gl as unknown as WebGLRenderingContext)
            : null) as typeof HTMLCanvasElement.prototype.getContext,
      );
      vi.spyOn(window, "getComputedStyle").mockReturnValue({
        getPropertyValue: () => "0",
      } as unknown as CSSStyleDeclaration);

      render(<SciFiBackgroundOidc />);
      expect(gl.linkProgram).not.toHaveBeenCalled();
    });

    it("responds to a window resize while the effects are active", () => {
      const ctx = make2dCtx();
      vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockImplementation(
        ((type: string) =>
          type === "2d"
            ? (ctx as unknown as CanvasRenderingContext2D)
            : null) as typeof HTMLCanvasElement.prototype.getContext,
      );
      render(<SciFiBackgroundOidc />);
      ctx.setTransform.mockClear();
      window.dispatchEvent(new Event("resize"));
      expect(ctx.setTransform).toHaveBeenCalled();
    });
  });
});
