using AutoMapper;
using ProductService.Application.DTOs;
using ProductService.Domain.Entities;

namespace ProductService.Application.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // Product mappings
            CreateMap<Product, ProductDto>()
                .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category!.Name))
                .ForMember(dest => dest.DepartmentName, opt => opt.MapFrom(src => src.Department!.Name))
                .ForMember(dest=>dest.ImageUrl,opt=>opt.MapFrom(src=>
                    !string.IsNullOrEmpty(src.ImageUrl) ? $"{src.ImageUrl}" : null));

            // Category mappings
            CreateMap<Category, CategoryDto>()
                .ForMember(dest => dest.ProductCount,
                    opt => opt.MapFrom(src => src.Products != null ? src.Products.Count : 0));

            CreateMap<CreateCategoryDto, Category>()
                .ConstructUsing(src => new Category(src.Name, src.Description,src.IsActive));

            // Department mappings
            CreateMap<Department, DepartmentDto>()
                .ForMember(dest => dest.ProductCount,
                    opt => opt.MapFrom(src => src.Products != null ? src.Products.Count : 0))
                .ForMember(dest => dest.WorkerCount,
                    opt => opt.MapFrom(src => src.Products != null
                        ? src.Products.Where(p => !string.IsNullOrEmpty(p.Worker))
                            .Select(p => p.Worker)
                            .Distinct()
                            .Count()
                        : 0));

            CreateMap<CreateDepartmentDto, Department>()
                .ConstructUsing(src => new Department(src.Name, src.Description,src.IsActive));
        }
    }
}
