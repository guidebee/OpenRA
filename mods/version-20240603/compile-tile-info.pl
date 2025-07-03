#!/usr/bin/env perl

use strict;
use warnings;

use YAML qw(LoadFile DumpFile);
use JSON::PP qw(encode_json);
use Readonly;

Readonly my $tileset_yaml => "temperat.yaml";
Readonly my $template_paths_yaml => "temperat-template-paths.yaml";
Readonly my $entity_info_yaml => "entity-info.yaml";
Readonly my $obstacle_info_yaml => "obstacle-info.yaml";
Readonly my $output_yaml => "temperat-info.yaml";
Readonly my $output_json => "temperat-info.json";

my $tileset = LoadFile($tileset_yaml);
my $template_paths = LoadFile($template_paths_yaml);
my $entity_info = LoadFile($entity_info_yaml);
my $obstacle_info = LoadFile($obstacle_info_yaml);

my $tile_info = {};

for my $template (values %{$tileset->{Templates}}) {
    my $id = $template->{Id};

    my $pickany = $template->{PickAny};
    my ($size_x, $size_y) = ($template->{Size} =~ /^(\d+),(\d+)$/);
    die "Bad size $template->{Size}" if !defined($size_y);
    die "Expected size 1,1 for PickAny" if $pickany && ($size_x != 1 || $size_y != 1);
    for (my $y = 0; $y < $size_y; $y++) {
        for (my $x = 0; $x < $size_x; $x++) {
            my $index = $x + $y * $size_x;
            my $type = $template->{Tiles}->{$index};
            my $ti = $pickany ? "t${id}" : "t${id}i${index}";

            if (!defined($type)) {
                next;
            }
            my $codes;
            if ($pickany) {
                $codes = [];
                for my $altindex (sort {$a <=> $b} (keys %{$template->{Tiles}})) {
                    push @$codes, "t${id}i${altindex}";
                }
            } else {
                $codes = [$ti];
            }
            $tile_info->{$ti} = {
                Type => $type,
                Codes => $codes,
            };
        }
    }
}

my $output = {
    Tileset => $tileset,
    TileInfo => $tile_info,
    TemplatePaths => $template_paths,
    EntityInfo => $entity_info,
    ObstacleInfo => $obstacle_info,
};

DumpFile($output_yaml, $output);
{
    my $json = encode_json($output);
    open(my $f, '>', $output_json) || die "Bad open: $!";
    print $f $json;
    close($f);
}
