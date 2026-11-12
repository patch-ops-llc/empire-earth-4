shader_type spatial;

// A forest or cliff tile under the fog of war. `fog` is a picture of the map with a pixel for a
// tile, whose color is the fog's and whose alpha is how much of the fog lies on the tile
// (FogPicture.cs). The whole shape takes the fog of the tile it stands on, which is the tile
// under its own origin, and on a tile the player has never seen it is not there at all.

uniform vec3 color : source_color = vec3(0.5);
uniform sampler2D fog : source_color, hint_default_transparent, filter_nearest, repeat_disable;
uniform vec2 map_size = vec2(1.0);

varying vec4 shade;

void vertex() {
	vec2 tile = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xz;
	shade = textureLod(fog, tile / map_size, 0.0);
	// It shrinks to nothing over the last part of the way to unexplored, so that no dark shape
	// is left standing in front of ground that is seen.
	VERTEX *= 1.0 - smoothstep(0.6, 0.95, shade.a);
}

void fragment() {
	ALBEDO = mix(color, shade.rgb, shade.a);
	ROUGHNESS = 1.0;
}
