using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;


namespace PlateauResoniteLink.Tests.Profiles;

public sealed class GeneratedLod1RoofCityObjectFactoryTests
{
    [Theory]
    [InlineData("3003")]
    [InlineData("3004")]
    public void CreateReplacesLod1NoWallBuildingWithThinRoofSlab(string classCode)
    {
        TexturePayload texture = CreateTexturePayload("textures/no-wall-top.png");
        ParsedSurface top = CreateSurface(
            "lod1-top",
            ParsedSurfaceSemantic.Roof,
            altitude: 10.0,
            uvs: CreateClosedQuadUvs(),
            texturePayload: texture);
        ParsedSurface bottom = CreateSurface(
            "lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            altitude: 0.0);
        ParsedSurface wall = CreateSurface(
            "lod1-wall",
            ParsedSurfaceSemantic.Wall,
            altitude: 4.0);
        ParsedCityObject cityObject = CreateCityObject(
            [top, bottom, wall],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                CityGmlClassCodes = [classCode],
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.DoesNotContain(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Wall);
        Assert.DoesNotContain(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Ground);
        Assert.Equal(6, generated.Surfaces.Length);

        ParsedSurface underside = Assert.Single(
            generated.Surfaces,
            static surface => ComputeResoniteNormal(surface).Y < -0.9);
        Assert.Same(texture, underside.TexturePayload);
        Assert.All(underside.ExteriorRing.Vertices, vertex => Assert.InRange(vertex.Altitude, 9.7 - 1e-8, 10.0 + 1e-8));
        Assert.True(ComputeParsedNormalY(underside) > 0.9);
        Assert.Equal(
            [new Float2(0.0, 1.0), new Float2(1.0, 1.0), new Float2(1.0, 0.0), new Float2(0.0, 0.0), new Float2(0.0, 1.0)],
            underside.ExteriorRing.UVs);

        ParsedSurface side = Assert.Single(
            generated.Surfaces,
            static surface => ComputeResoniteNormal(surface).Z < -0.9);
        Assert.Same(texture, side.TexturePayload);
        Assert.True(ComputeParsedNormalY(side) is < 0.1 and > -0.1);
        Assert.Equal(
            [new Float2(0.0, 0.0), new Float2(0.0, 0.0), new Float2(1.0, 0.0), new Float2(1.0, 0.0), new Float2(0.0, 0.0)],
            side.ExteriorRing.UVs);
        Float3 resoniteSideNormal = ComputeResoniteNormal(side);
        Assert.True(resoniteSideNormal.Z < -0.9);
    }

    [Fact]
    public void CreateReplacesRoofOnlyLod1NoWallBuildingWithThinRoofSlab()
    {
        TexturePayload texture = CreateTexturePayload("textures/no-wall-lod1-roof-only.png");
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface(
                    "lod1-roof-only",
                    ParsedSurfaceSemantic.Roof,
                    CreateClosedQuadVertices(altitude: 10.0, longitudeOffset: 0.0, longitudeWidth: 0.00020),
                    CreateClosedQuadUvs(),
                    texture),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] },
            lodLevel: 1);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.NotSame(cityObject, generated);
        Assert.Equal(6, generated.Surfaces.Length);
        ParsedSurface generatedTop = Assert.Single(generated.Surfaces, static surface => ComputeResoniteNormal(surface).Y > 0.9);
        Assert.Same(texture, generatedTop.TexturePayload);
        Assert.Single(generated.Surfaces, static surface => ComputeResoniteNormal(surface).Y < -0.9);
        Assert.Equal(4, generated.Surfaces.Count(static surface => Math.Abs(ComputeResoniteNormal(surface).Y) < 0.1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateOrientsLod1NoWallTopSurfaceForResoniteOutput(bool reverseSourceRing)
    {
        TexturePayload texture = CreateTexturePayload("textures/no-wall-top.png");
        GeodeticPoint[] vertices = CreateClosedQuadVertices(altitude: 10.0, longitudeOffset: 0.0, longitudeWidth: 0.00020);
        Float2[] uvs = CreateClosedQuadUvs();
        if (reverseSourceRing)
        {
            vertices = ReverseClosedRing(vertices);
            uvs = ReverseClosedUvs(uvs);
        }

        ParsedSurface top = CreateSurface(
            "lod1-top",
            ParsedSurfaceSemantic.Roof,
            vertices,
            uvs,
            texture);
        ParsedCityObject cityObject = CreateCityObject(
            [
                top,
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
                CreateSurface("lod1-wall", ParsedSurfaceSemantic.Wall, altitude: 4.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] },
            lodLevel: 1);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        ParsedSurface generatedTop = Assert.Single(generated.Surfaces, static surface => ComputeResoniteNormal(surface).Y > 0.9);
        Assert.Same(texture, generatedTop.TexturePayload);
        Assert.True(ComputeResoniteNormal(generatedTop).Y > 0.9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatePreservesLod2NoWallTopSurfaceWinding(bool reverseSourceRing)
    {
        TexturePayload texture = CreateTexturePayload("textures/no-wall-top.png");
        GeodeticPoint[] vertices = CreateClosedQuadVertices(altitude: 10.0, longitudeOffset: 0.0, longitudeWidth: 0.00020);
        Float2[] uvs = CreateClosedQuadUvs();
        if (reverseSourceRing)
        {
            vertices = ReverseClosedRing(vertices);
            uvs = ReverseClosedUvs(uvs);
        }

        ParsedSurface top = CreateSurface(
            "lod2-top",
            ParsedSurfaceSemantic.Roof,
            vertices,
            uvs,
            texture);
        ParsedCityObject cityObject = CreateCityObject(
            [
                top,
                CreateSurface("lod2-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
                CreateSurface("lod2-wall", ParsedSurfaceSemantic.Wall, altitude: 4.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] },
            lodLevel: 2);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        ParsedSurface generatedTop = Assert.Single(generated.Surfaces, surface => ReferenceEquals(surface, top));
        Assert.Same(top, generatedTop);
        Assert.Equal(vertices, generatedTop.ExteriorRing.Vertices);
        Assert.Equal(uvs, generatedTop.ExteriorRing.UVs);
        Assert.Same(texture, generatedTop.TexturePayload);
    }

    [Fact]
    public void CreateReplacesNonRectangularLod1NoWallBuildingWithThinRoofSlab()
    {
        ParsedSurface top = CreateSurface(
            "lod1-pentagon-top",
            ParsedSurfaceSemantic.Roof,
            CreateClosedPentagonVertices(altitude: 10.0));
        ParsedCityObject cityObject = CreateCityObject(
            [
                top,
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
                CreateSurface("lod1-wall", ParsedSurfaceSemantic.Wall, altitude: 4.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] });

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.DoesNotContain(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Wall);
        Assert.DoesNotContain(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Ground);
        Assert.Equal(7, generated.Surfaces.Length);
        Assert.Equal(5, generated.Surfaces.Count(static surface => Math.Abs(ComputeResoniteNormal(surface).Y) < 0.1));
        Assert.Single(generated.Surfaces, static surface => ComputeResoniteNormal(surface).Y < -0.9);
    }

    [Fact]
    public void CreateReplacesMultipleLod1NoWallTopSurfacesAndSkipsSharedSideEdges()
    {
        ParsedSurface roofA = CreateSurface(
            "lod1-roof-a",
            ParsedSurfaceSemantic.Roof,
            CreateClosedQuadVertices(altitude: 10.0, longitudeOffset: 0.0, longitudeWidth: 0.00010),
            CreateClosedQuadUvs());
        ParsedSurface roofB = CreateSurface(
            "lod1-roof-b",
            ParsedSurfaceSemantic.Roof,
            CreateClosedQuadVertices(altitude: 10.0, longitudeOffset: 0.00010, longitudeWidth: 0.00010),
            CreateClosedQuadUvs());
        ParsedCityObject cityObject = CreateCityObject(
            [
                roofA,
                roofB,
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
                CreateSurface("lod1-wall", ParsedSurfaceSemantic.Wall, altitude: 4.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] });

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.Equal(10, generated.Surfaces.Length);
        Assert.Equal(6, generated.Surfaces.Count(static surface => Math.Abs(ComputeResoniteNormal(surface).Y) < 0.1));
        Assert.Equal(2, generated.Surfaces.Count(static surface => ComputeResoniteNormal(surface).Y < -0.9));
    }

    [Fact]
    public void CreateLeavesLod1NoWallBuildingWithTopInteriorRingUnchanged()
    {
        ParsedSurface top = CreateSurfaceWithInteriorRing(
            "lod1-top-with-hole",
            ParsedSurfaceSemantic.Roof,
            altitude: 10.0);
        ParsedCityObject cityObject = CreateCityObject(
            [
                top,
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
                CreateSurface("lod1-wall", ParsedSurfaceSemantic.Wall, altitude: 4.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] });

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.Same(cityObject, generated);
    }

    [Theory]
    [InlineData("3003")]
    [InlineData("3004")]
    public void CreateKeepsLod2NoWallRoofTopologyAndDropsWallAndGroundSurfaces(string classCode)
    {
        TexturePayload texture = CreateTexturePayload("textures/no-wall-lod2.png");
        ParsedSurface roofA = CreateSurface(
            "lod2-roof-a",
            ParsedSurfaceSemantic.Roof,
            CreateClosedQuadVertices(altitude: 10.0, longitudeOffset: 0.0, longitudeWidth: 0.00010),
            CreateClosedQuadUvs(),
            texture);
        ParsedSurface roofB = CreateSurface(
            "lod2-roof-b",
            ParsedSurfaceSemantic.Roof,
            CreateClosedQuadVertices(altitude: 10.0, longitudeOffset: 0.00010, longitudeWidth: 0.00010),
            CreateClosedQuadUvs(),
            texture);
        ParsedCityObject cityObject = CreateCityObject(
            [
                roofA,
                roofB,
                CreateSurface("lod2-wall", ParsedSurfaceSemantic.Wall, altitude: 5.0),
                CreateSurface("lod2-ground", ParsedSurfaceSemantic.Ground, altitude: 0.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = [classCode] },
            lodLevel: 2);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.Contains(generated.Surfaces, surface => ReferenceEquals(surface, roofA));
        Assert.Contains(generated.Surfaces, surface => ReferenceEquals(surface, roofB));
        Assert.DoesNotContain(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Wall);
        Assert.DoesNotContain(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Ground);
        Assert.Equal(10, generated.Surfaces.Length);
        Assert.Equal(6, generated.Surfaces.Count(static surface => Math.Abs(ComputeResoniteNormal(surface).Y) < 0.1));
        Assert.Equal(2, generated.Surfaces.Count(static surface => ComputeResoniteNormal(surface).Y < -0.9));
        Assert.All(generated.Surfaces, static surface => Assert.Equal(ParsedSurfaceSemantic.Roof, surface.Semantic));

        Assert.Equal(6, generated.Surfaces.Count(static surface => Math.Abs(ComputeResoniteNormal(surface).Y) < 0.1));
    }

    [Fact]
    public void CreateGeneratesLod2NoWallSlabForRoofOnlyInput()
    {
        TexturePayload texture = CreateTexturePayload("textures/no-wall-lod2-roof-only.png");
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface(
                    "lod2-roof-only",
                    ParsedSurfaceSemantic.Roof,
                    CreateClosedQuadVertices(altitude: 10.0, longitudeOffset: 0.0, longitudeWidth: 0.00020),
                    CreateClosedQuadUvs(),
                    texture),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] },
            lodLevel: 2);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.NotSame(cityObject, generated);
        Assert.Equal(6, generated.Surfaces.Length);
        ParsedSurface generatedTop = Assert.Single(generated.Surfaces, static surface => ComputeResoniteNormal(surface).Y > 0.9);
        Assert.Same(texture, generatedTop.TexturePayload);
        Assert.Single(generated.Surfaces, static surface => ComputeResoniteNormal(surface).Y < -0.9);
        Assert.Equal(4, generated.Surfaces.Count(static surface => Math.Abs(ComputeResoniteNormal(surface).Y) < 0.1));
    }

    [Fact]
    public void CreateGeneratesLod2NoWallSlabWithoutTreatingLowerNonRoofSurfacesAsRealWalls()
    {
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface("lod2-roof", ParsedSurfaceSemantic.Roof, altitude: 10.0),
                CreateSurface("lod2-ground", ParsedSurfaceSemantic.Ground, altitude: 0.0),
                CreateSurface(
                    "lod2-sloped-near-bottom",
                    ParsedSurfaceSemantic.Ground,
                    CreateSlopedClosedQuadVertices(minimumAltitude: 9.0, maximumAltitude: 9.85)),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] },
            lodLevel: 2);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.NotSame(cityObject, generated);
        Assert.DoesNotContain(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Ground);
        Assert.Single(generated.Surfaces, static surface => ComputeResoniteNormal(surface).Y < -0.9);
    }

    [Fact]
    public void CreateGeneratesLod2NoWallSlabWithoutTreatingSlopedLowerNonRoofSurfacesAsRealWalls()
    {
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface(
                    "lod2-roof",
                    ParsedSurfaceSemantic.Roof,
                    CreateSlopedClosedQuadVertices(minimumAltitude: 10.0, maximumAltitude: 12.0)),
                CreateSurface(
                    "lod2-detail-under-sloped-roof",
                    ParsedSurfaceSemantic.Wall,
                    CreateSlopedClosedQuadVertices(minimumAltitude: 11.0, maximumAltitude: 11.2)),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] },
            lodLevel: 2);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.NotSame(cityObject, generated);
        Assert.DoesNotContain(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Wall);
        Assert.Single(generated.Surfaces, static surface => ComputeResoniteNormal(surface).Y < -0.9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3001")]
    [InlineData("3002")]
    [InlineData("3000")]
    [InlineData("30030")]
    [InlineData("x3003")]
    public void CreateDoesNotInferNoWallBuildingFromMissingOrNonExactClassCode(string classCode)
    {
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface("lod2-roof", ParsedSurfaceSemantic.Roof, altitude: 10.0),
                CreateSurface("lod2-wall", ParsedSurfaceSemantic.Wall, altitude: 5.0),
                CreateSurface("lod2-ground", ParsedSurfaceSemantic.Ground, altitude: 0.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                CityGmlClassCodes = string.IsNullOrEmpty(classCode) ? [] : [classCode],
            },
            lodLevel: 2);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.Same(cityObject, generated);
    }

    [Fact]
    public void CreateLeavesNonNoWallLod2BuildingBeforeResolvingLocalOrigin()
    {
        ParsedSurface emptyRoof = new(
            ParsedSurfaceSemantic.Roof,
            new ParsedRing([], null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
        ParsedCityObject cityObject = CreateCityObject(
            [emptyRoof],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty,
            lodLevel: 2);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.Same(cityObject, generated);
    }

    [Fact]
    public void CreateLeavesNoWallLod2BuildingWithEmptyGeometryBeforeResolvingLocalOrigin()
    {
        ParsedSurface emptyRoof = new(
            ParsedSurfaceSemantic.Roof,
            new ParsedRing([], null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
        ParsedCityObject cityObject = CreateCityObject(
            [emptyRoof],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] },
            lodLevel: 2);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.Same(cityObject, generated);
    }

    [Fact]
    public void CreateLeavesLod2NoWallBuildingWithoutRoofSurfacesUnchanged()
    {
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface("lod2-wall", ParsedSurfaceSemantic.Wall, altitude: 5.0),
                CreateSurface("lod2-ground", ParsedSurfaceSemantic.Ground, altitude: 0.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with { CityGmlClassCodes = ["3003"] },
            lodLevel: 2);

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.Same(cityObject, generated);
    }

    [Fact]
    public void CreateReplacesSingleTexturelessTopSurfaceWithGeneratedRoofSurfaces()
    {
        ParsedSurface top = CreateSurface(
            "lod1-top",
            ParsedSurfaceSemantic.Roof,
            altitude: 10.0);
        ParsedSurface bottom = CreateSurface(
            "lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            altitude: 0.0);
        ParsedCityObject cityObject = CreateCityObject(
            [top, bottom],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.DoesNotContain(generated.Surfaces, surface => ReferenceEquals(surface, top));
        Assert.Contains(generated.Surfaces, surface => ReferenceEquals(surface, bottom));
        Assert.Equal(5, generated.Surfaces.Length);
        Assert.Equal(4, generated.Surfaces.Count(surface => !ReferenceEquals(surface, bottom)));
        Assert.All(
            generated.Surfaces.Where(surface => !ReferenceEquals(surface, bottom)),
            static surface => Assert.Null(surface.TexturePayload));
    }

    [Fact]
    public void CreateSkipsEmptySurfacesBeforeSelectingGeneratedRoofTopCandidate()
    {
        ParsedSurface empty = CreateSurface("empty", ParsedSurfaceSemantic.Roof, [], null, texturePayload: null);
        ParsedSurface top = CreateSurface(
            "lod1-top",
            ParsedSurfaceSemantic.Roof,
            altitude: 10.0);
        ParsedSurface bottom = CreateSurface(
            "lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            altitude: 0.0);
        ParsedCityObject cityObject = CreateCityObject(
            [empty, top, bottom],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.DoesNotContain(generated.Surfaces, surface => ReferenceEquals(surface, top));
        Assert.Contains(generated.Surfaces, surface => ReferenceEquals(surface, empty));
        Assert.Equal(6, generated.Surfaces.Length);
        Assert.Equal(4, generated.Surfaces.Count(surface => !ReferenceEquals(surface, empty) && !ReferenceEquals(surface, bottom)));
    }

    [Fact]
    public void CreateDoesNotUseRoofLikeSurfaceNameAsGenerationState()
    {
        const string topSurfaceName = "lod1-top_roof_shed-roof";
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface(topSurfaceName, ParsedSurfaceSemantic.Roof, altitude: 10.0),
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.NotSame(cityObject, generated);
        Assert.Equal(5, generated.Surfaces.Length);
        Assert.Equal(4, generated.Surfaces.Count(static surface => surface.Semantic != ParsedSurfaceSemantic.Ground));
        Assert.Single(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Ground);
    }

    [Fact]
    public void CreateDoesNotUseNestedRoofLikeSurfaceNameAsGenerationState()
    {
        const string topSurfaceName = "source_roof_part_roof_shed-roof";
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface(topSurfaceName, ParsedSurfaceSemantic.Roof, altitude: 10.0),
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.NotSame(cityObject, generated);
        Assert.Equal(5, generated.Surfaces.Length);
        Assert.Equal(4, generated.Surfaces.Count(static surface => surface.Semantic != ParsedSurfaceSemantic.Ground));
        Assert.Single(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Ground);
    }

    [Fact]
    public void CreateDoesNotTreatOtherSurfaceNamesAsGeneratedRoofState()
    {
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface("lod1-top", ParsedSurfaceSemantic.Roof, altitude: 10.0),
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
                CreateSurface("tran_road-marking", ParsedSurfaceSemantic.Wall, altitude: 1.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.NotSame(cityObject, generated);
        Assert.Contains(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Wall);
        Assert.Contains(generated.Surfaces, static surface => surface.Semantic == ParsedSurfaceSemantic.Roof);
    }

    [Fact]
    public void CreateSkipsNonGeographicObject()
    {
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface("lod1-top", ParsedSurfaceSemantic.Roof, altitude: 10.0),
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
            ],
            CoordinateReferenceSystem.Parse((string?)null),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = CreateGeneratedCityObject(cityObject);

        Assert.Same(cityObject, generated);
    }

    private static ParsedCityObject CreateCityObject(
        ParsedSurface[] surfaces,
        CoordinateReferenceSystem referenceSystem,
        BuildingAttributeContext attributes,
        int? lodLevel = 1)
    {
        return new ParsedCityObject(
            SlotKey: "bldg-lod1",
            DisplayName: "bldg-lod1",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: lodLevel,
            Surfaces: surfaces,
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: "udx/bldg/53394525/bldg.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: attributes);
    }

    private static ParsedSurface CreateSurface(
        string polygonId,
        ParsedSurfaceSemantic semantic,
        double altitude,
        IReadOnlyList<Float2>? uvs = null,
        TexturePayload? texturePayload = null)
    {
        GeodeticPoint[] vertices = CreateClosedQuadVertices(altitude, longitudeOffset: 0.0, longitudeWidth: 0.00020);
        return CreateSurface(polygonId, semantic, vertices, uvs, texturePayload);
    }

    private static ParsedSurface CreateSurface(
        string polygonId,
        ParsedSurfaceSemantic semantic,
        GeodeticPoint[] vertices,
        IReadOnlyList<Float2>? uvs = null,
        TexturePayload? texturePayload = null)
    {
        _ = polygonId;
        return new ParsedSurface(
            semantic,
            new ParsedRing(vertices, uvs),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            texturePayload);
    }

    private static ParsedSurface CreateSurfaceWithInteriorRing(
        string polygonId,
        ParsedSurfaceSemantic semantic,
        double altitude)
    {
        _ = polygonId;
        return new ParsedSurface(
            Semantic: semantic,
            ExteriorRing: new ParsedRing(CreateClosedQuadVertices(altitude, longitudeOffset: 0.0, longitudeWidth: 0.00020), null),
            InteriorRings:
            [
                new ParsedRing(CreateClosedQuadVertices(altitude, longitudeOffset: 0.00005, longitudeWidth: 0.00005),
                    null),
            ],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }

    private static GeodeticPoint[] CreateClosedQuadVertices(
        double altitude,
        double longitudeOffset,
        double longitudeWidth)
    {
        return
        [
            new(35.0, 139.0 + longitudeOffset, altitude),
            new(35.0, 139.0 + longitudeOffset + longitudeWidth, altitude),
            new(35.00010, 139.0 + longitudeOffset + longitudeWidth, altitude),
            new(35.00010, 139.0 + longitudeOffset, altitude),
            new(35.0, 139.0 + longitudeOffset, altitude),
        ];
    }

    private static GeodeticPoint[] CreateSlopedClosedQuadVertices(
        double minimumAltitude,
        double maximumAltitude)
    {
        return
        [
            new(35.0, 139.0, minimumAltitude),
            new(35.0, 139.00020, maximumAltitude),
            new(35.00010, 139.00020, maximumAltitude),
            new(35.00010, 139.0, minimumAltitude),
            new(35.0, 139.0, minimumAltitude),
        ];
    }

    private static GeodeticPoint[] CreateClosedPentagonVertices(double altitude)
    {
        return
        [
            new(35.0, 139.0, altitude),
            new(35.0, 139.00020, altitude),
            new(35.00008, 139.00025, altitude),
            new(35.00015, 139.00010, altitude),
            new(35.00010, 139.0, altitude),
            new(35.0, 139.0, altitude),
        ];
    }

    private static Float2[] CreateClosedQuadUvs()
    {
        return
        [
            new Float2(0.0, 0.0),
            new Float2(1.0, 0.0),
            new Float2(1.0, 1.0),
            new Float2(0.0, 1.0),
            new Float2(0.0, 0.0),
        ];
    }

    private static GeodeticPoint[] ReverseClosedRing(GeodeticPoint[] vertices)
    {
        GeodeticPoint[] reversed = vertices.Take(vertices.Length - 1).Reverse().ToArray();
        return [.. reversed, reversed[0]];
    }

    private static Float2[] ReverseClosedUvs(Float2[] uvs)
    {
        Float2[] reversed = uvs.Take(uvs.Length - 1).Reverse().ToArray();
        return [.. reversed, reversed[0]];
    }

    private static TexturePayload CreateTexturePayload(string identity)
    {
        return new RawRgba32TexturePayload(1, 1, "sRGB", [255, 255, 255, 255], identity);
    }

    private static ParsedCityObject CreateGeneratedCityObject(ParsedCityObject cityObject)
    {
        return GeneratedLod1RoofCityObjectFactory.CreateDraft(cityObject).Source;
    }

    private static double ComputeParsedNormalY(ParsedSurface surface)
    {
        GeodeticPoint origin = surface.ExteriorRing.Vertices[0];
        LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, Geocentric.WGS84);
        return CityGmlSurfaceProjectionPolicy.ComputeSurfaceNormal(surface, origin, cartesian)?.Y ?? double.NaN;
    }

    private static Float3 ComputeResoniteNormal(ParsedSurface surface)
    {
        GeodeticPoint origin = surface.ExteriorRing.Vertices[0];
        LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude, Geocentric.WGS84);
        Float3[] positions = surface.ExteriorRing.Vertices
            .Take(3)
            .Select(point => SceneAxisMapper.CreatePosition(
                point.Latitude,
                point.Longitude,
                point.Altitude,
                origin.Latitude,
                origin.Longitude,
                origin.Altitude,
                cartesian))
            .ToArray();
        return Normalize(Cross(Subtract(positions[2], positions[0]), Subtract(positions[1], positions[0])));
    }

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static Float3 Cross(Float3 left, Float3 right)
    {
        return new Float3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static Float3 Normalize(Float3 value)
    {
        double length = System.Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        return new Float3(value.X / length, value.Y / length, value.Z / length);
    }
}
