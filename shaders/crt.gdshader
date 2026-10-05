// Light CRT pass: barrel curve, scanlines, chromatic fringe, vignette.
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
uniform float curvature = 0.035;
uniform float scanline_strength = 0.14;
uniform float aberration = 0.0012;
uniform float vignette = 0.45;

void fragment() {
	vec2 c = SCREEN_UV * 2.0 - 1.0;
	c *= 1.0 + curvature * dot(c, c);
	vec2 uv = c * 0.5 + 0.5;
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
		COLOR = vec4(0.0, 0.0, 0.0, 1.0);
	} else {
		float r = texture(screen_texture, uv + vec2(aberration, 0.0)).r;
		float g = texture(screen_texture, uv).g;
		float b = texture(screen_texture, uv - vec2(aberration, 0.0)).b;
		vec3 col = vec3(r, g, b);
		float scan = 0.5 + 0.5 * sin(FRAGCOORD.y * 3.14159);
		col *= 1.0 - scanline_strength * scan;
		col *= 1.0 - vignette * dot(c, c) * 0.5;
		COLOR = vec4(col, 1.0);
	}
}
