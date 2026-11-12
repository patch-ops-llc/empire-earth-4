shader_type spatial;

// Flat ground with one grid line per tile edge and a heavier line every `major_every` tiles.
// One world unit is one simulation tile, so the lines sit on whole world coordinates.
//
// The fog of war lies on it: `fog` is a picture of the map with a pixel for a tile, whose color
// is the fog's and whose alpha is how much of the fog lies on the tile (FogPicture.cs). Without
// a picture there is no fog.

uniform vec3 ground_color : source_color = vec3(0.33, 0.45, 0.26);
uniform vec3 minor_line_color : source_color = vec3(0.27, 0.38, 0.22);
uniform vec3 major_line_color : source_color = vec3(0.16, 0.24, 0.14);
uniform float major_every = 8.0;
uniform float minor_line_width = 0.02;
uniform float major_line_width = 0.05;
uniform sampler2D fog : source_color, hint_default_transparent, filter_linear, repeat_disable;
uniform vec2 map_size = vec2(1.0);

varying vec2 tile_position;

void vertex() {
	tile_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

// Share of the pixel covered by grid lines `spacing` tiles apart and `width` tiles wide.
float grid_coverage(vec2 position, float spacing, float width) {
	vec2 to_nearest_line = abs(fract(position / spacing - 0.5) - 0.5) * spacing;
	vec2 pixel = fwidth(position);
	vec2 half_width = vec2(width * 0.5);
	vec2 covered = 1.0 - smoothstep(half_width - pixel, half_width + pixel, to_nearest_line);
	// Lines closer together than about two pixels would shimmer, so they fade out instead.
	float fade = 1.0 - smoothstep(0.25, 0.5, max(pixel.x, pixel.y) / spacing);
	return max(covered.x, covered.y) * fade;
}

void fragment() {
	float minor = grid_coverage(tile_position, 1.0, minor_line_width);
	float major = grid_coverage(tile_position, major_every, major_line_width);
	vec3 color = mix(ground_color, minor_line_color, minor);
	color = mix(color, major_line_color, major);
	vec4 shade = texture(fog, tile_position / map_size);
	ALBEDO = mix(color, shade.rgb, shade.a);
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}
