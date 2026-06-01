import { useEffect, useRef } from "react";

function readCssNum(el: HTMLElement, name: string, fallback: number): number {
  const v = getComputedStyle(el).getPropertyValue(name).trim();
  const n = parseFloat(v);
  return Number.isFinite(n) ? n : fallback;
}

export function SciFiBackgroundOidc() {
  const rootRef  = useRef<HTMLDivElement>(null);
  const atmRef   = useRef<HTMLCanvasElement>(null);
  const webglRef = useRef<HTMLCanvasElement>(null);

  /* 2-D atmospheric canvas */
  useEffect(() => {
    const canvas = atmRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    let t = 0, raf: number, w = 0, h = 0, dpr = 1;

    function hslToRgb(hue: number, s: number, l: number) {
      s /= 100; l /= 100;
      const k = (n: number) => (n + hue / 30) % 12;
      const a = s * Math.min(l, 1 - l);
      const f = (n: number) =>
        l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
      return [
        Math.round(f(0) * 255),
        Math.round(f(8) * 255),
        Math.round(f(4) * 255),
      ];
    }

    function resize() {
      dpr = window.devicePixelRatio || 1;
      w = canvas!.width  = Math.floor(window.innerWidth  * dpr);
      h = canvas!.height = Math.floor(window.innerHeight * dpr);
      canvas!.style.width  = window.innerWidth  + "px";
      canvas!.style.height = window.innerHeight + "px";
      ctx!.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    function draw() {
      const time    = t * 0.008;
      const baseHue = 200 + 15 * Math.sin(time);
      const cx = (w / dpr) * 0.5;
      const cy = (h / dpr) * 0.5;
      ctx!.clearRect(0, 0, w / dpr, h / dpr);

      const colors: number[][] = [
        hslToRgb(baseHue,      100, 50),
        hslToRgb(baseHue + 15, 100, 50),
        hslToRgb(baseHue - 15, 100, 50),
      ];
      const positions: Array<[number, number, number, number]> = [
        [cx * 0.6, cy * 0.7, Math.max(w, h) / dpr * 0.6, 0.18],
        [cx * 1.3, cy * 0.4, Math.max(w, h) / dpr * 0.5, 0.12],
        [cx * 0.3, cy * 1.2, Math.max(w, h) / dpr * 0.45, 0.1],
      ];

      positions.forEach(([x, y, r, alpha], i) => {
        const g = ctx!.createRadialGradient(x, y, 0, x, y, r);
        const [red, green, blue] = colors[i];
        g.addColorStop(0, `rgba(${red},${green},${blue},${alpha})`);
        g.addColorStop(1, `rgba(${red},${green},${blue},0)`);
        ctx!.fillStyle = g;
        ctx!.fillRect(0, 0, w / dpr, h / dpr);
      });

      t++;
      raf = requestAnimationFrame(draw);
    }

    resize();
    window.addEventListener("resize", resize);
    draw();
    return () => { cancelAnimationFrame(raf); window.removeEventListener("resize", resize); };
  }, []);

  /* WebGL plasma — skip in light mode */
  useEffect(() => {
    const canvas = webglRef.current;
    const root   = rootRef.current;
    if (!canvas || !root) return;

    const enabled = readCssNum(root, "--atm-webgl-opacity", 0.5) > 0;
    if (!enabled) return;

    const gl = canvas.getContext("webgl");
    if (!gl) return;

    let raf: number;

    function resize() {
      canvas!.width  = window.innerWidth;
      canvas!.height = window.innerHeight;
      gl!.viewport(0, 0, canvas!.width, canvas!.height);
    }

    const vsSource = `attribute vec2 position;void main(){gl_Position=vec4(position,0.0,1.0);}`;
    const fsSource = `precision mediump float;uniform vec2 u_res;uniform float u_time;void main(){vec2 uv=gl_FragCoord.xy/u_res;float d=sin(uv.x*3.0+u_time*0.2)*cos(uv.y*2.0+u_time*0.15)+sin(uv.y*4.0-u_time*0.1)*0.5;float b=0.03+0.02*d;gl_FragColor=vec4(b*0.5,b*0.8,b*1.2,1.0);}`;

    function makeShader(type: number, src: string) {
      const s = gl!.createShader(type)!;
      gl!.shaderSource(s, src);
      gl!.compileShader(s);
      return s;
    }

    const prog = gl.createProgram()!;
    gl.attachShader(prog, makeShader(gl.VERTEX_SHADER, vsSource));
    gl.attachShader(prog, makeShader(gl.FRAGMENT_SHADER, fsSource));
    gl.linkProgram(prog);
    gl.useProgram(prog);

    const buf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER,
      new Float32Array([-1,-1, 1,-1, -1,1, -1,1, 1,-1, 1,1]),
      gl.STATIC_DRAW);
    const pos = gl.getAttribLocation(prog, "position");
    gl.enableVertexAttribArray(pos);
    gl.vertexAttribPointer(pos, 2, gl.FLOAT, false, 0, 0);

    const timeLoc = gl.getUniformLocation(prog, "u_time");
    const resLoc  = gl.getUniformLocation(prog, "u_res");

    function render(t: number) {
      gl!.uniform1f(timeLoc, t * 0.001);
      gl!.uniform2f(resLoc, canvas!.width, canvas!.height);
      gl!.drawArrays(gl!.TRIANGLES, 0, 6);
      raf = requestAnimationFrame(render);
    }

    resize();
    window.addEventListener("resize", resize);
    raf = requestAnimationFrame(render);
    return () => { cancelAnimationFrame(raf); window.removeEventListener("resize", resize); };
  }, []);

  return (
    <div ref={rootRef}>
      {/* Grid */}
      <div
        className="fixed inset-0 pointer-events-none z-0"
        style={{
          background:
            "linear-gradient(90deg,transparent 49.8%,var(--grid-line) 50%,transparent 50.2%)," +
            "linear-gradient(0deg,transparent 49.8%,var(--grid-line) 50%,transparent 50.2%)",
          backgroundSize: "80px 80px",
          animation: "oidc-gridPulse 8s ease-in-out infinite",
        }}
      />

      {/* Scan line */}
      <div
        className="fixed left-0 right-0 pointer-events-none z-50"
        style={{
          top: -2,
          height: "1.5px",
          background:
            "linear-gradient(90deg,transparent 5%,var(--accent) 50%,transparent 95%)",
          animation: "oidc-scanMove 7s linear infinite",
          opacity: "var(--atm-scan-opacity)",
          filter: "blur(0.3px)",
        }}
      />

      {/* Radial glow */}
      <div
        className="fixed pointer-events-none z-0"
        style={{
          top: "55%", left: "25%",
          transform: "translate(-50%,-50%)",
          width: 700, height: 700,
          background: "radial-gradient(ellipse,var(--accent2-glow) 0%,transparent 60%)",
          animation: "oidc-glowPulse 6s ease-in-out infinite",
          mixBlendMode: "screen",
        }}
      />

      {/* Atmospheric canvas */}
      <canvas
        ref={atmRef}
        className="fixed inset-0 pointer-events-none z-0"
        style={{
          opacity: "var(--atm-canvas-opacity)",
          mixBlendMode: "var(--atm-canvas-blend)" as React.CSSProperties["mixBlendMode"],
        }}
      />

      {/* WebGL plasma */}
      <canvas
        ref={webglRef}
        className="fixed inset-0 pointer-events-none z-0"
        style={{
          opacity: "var(--atm-webgl-opacity)",
          mixBlendMode: "screen",
        }}
      />

      {/* Corner brackets */}
      <div className="corner corner-tl fixed w-12 h-12 pointer-events-none z-[100]"
        style={{ top: 20, left: 20, borderTop: "1.5px solid var(--accent)", borderLeft: "1.5px solid var(--accent)", opacity: "var(--atm-corner-opacity)" }} />
      <div className="corner corner-tr fixed w-12 h-12 pointer-events-none z-[100]"
        style={{ top: 20, right: 20, borderTop: "1.5px solid var(--accent)", borderRight: "1.5px solid var(--accent)", opacity: "var(--atm-corner-opacity)" }} />
      <div className="corner corner-bl fixed w-12 h-12 pointer-events-none z-[100]"
        style={{ bottom: 20, left: 20, borderBottom: "1.5px solid var(--accent)", borderLeft: "1.5px solid var(--accent)", opacity: "var(--atm-corner-opacity)" }} />
      <div className="corner corner-br fixed w-12 h-12 pointer-events-none z-[100]"
        style={{ bottom: 20, right: 20, borderBottom: "1.5px solid var(--accent)", borderRight: "1.5px solid var(--accent)", opacity: "var(--atm-corner-opacity)" }} />

      {/* Corner dots */}
      {(["tl","tr","bl","br"] as const).map((pos) => (
        <div
          key={pos}
          className="fixed w-[3px] h-[3px] rounded-full pointer-events-none z-[100]"
          style={{
            background: "var(--accent)",
            opacity: "var(--atm-corner-dot-opacity)",
            animation: "oidc-dotPulse 4s ease-in-out infinite",
            ...(pos === "tl" ? { top: 18, left: 18 }
              : pos === "tr" ? { top: 18, right: 18 }
              : pos === "bl" ? { bottom: 18, left: 18 }
              : { bottom: 18, right: 18 }),
          }}
        />
      ))}
    </div>
  );
}
