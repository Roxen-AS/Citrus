// Citrus HEIC bridge. libheif/libde265 are dynamically linked and replaceable.
#include <libheif/heif.h>
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <vector>

static void check(heif_error error){if(error.code!=heif_error_Ok)throw std::runtime_error(error.message?error.message:"HEIC operation failed");}
static void size_check(int width,int height){if(width<=0||height<=0||width>16384||height>16384||uint64_t(width)*height>64000000)throw std::runtime_error("HEIC image exceeds 64 megapixels / 16384 pixels per side");}
static std::vector<uint8_t> read(const wchar_t* path,size_t maximum){std::ifstream file(std::filesystem::path(path),std::ios::binary|std::ios::ate);if(!file)throw std::runtime_error("Cannot open HEIC input");auto length=file.tellg();if(length<=0||uint64_t(length)>maximum)throw std::runtime_error("Invalid HEIC input size");std::vector<uint8_t> bytes(static_cast<size_t>(length));file.seekg(0);if(!file.read(reinterpret_cast<char*>(bytes.data()),length))throw std::runtime_error("Cannot read HEIC input");return bytes;}
static heif_error write_bytes(heif_context*,const void* bytes,size_t length,void* state){return fwrite(bytes,1,length,static_cast<FILE*>(state))==length?heif_error{heif_error_Ok,heif_suberror_Unspecified,"ok"}:heif_error{heif_error_Encoding_error,heif_suberror_Unspecified,"Cannot write HEIC output"};}
int wmain(int argc,wchar_t** argv)
{
    try
    {
        if(argc==2&&wcscmp(argv[1],L"--version")==0){std::cout<<"citrus-heic 1; libheif "<<heif_get_version()<<"; HEVC kvazaar/libde265\n";return 0;}
        std::unique_ptr<heif_context,decltype(&heif_context_free)> context(heif_context_alloc(),heif_context_free);
        auto limits=heif_context_get_security_limits(context.get());limits->max_image_size_pixels=64000000;limits->max_color_profile_size=4*1024*1024;limits->max_total_memory=1024ull*1024*1024;limits->max_sequence_frames=1;
        if(argc==4&&wcscmp(argv[1],L"decode")==0)
        {
            auto bytes=read(argv[2],512ull*1024*1024);check(heif_context_read_from_memory_without_copy(context.get(),bytes.data(),bytes.size(),nullptr));
            if(heif_context_get_number_of_top_level_images(context.get())!=1)throw std::runtime_error("Choose a single primary image, not an image sequence");
            heif_image_handle* raw_handle=nullptr;check(heif_context_get_primary_image_handle(context.get(),&raw_handle));std::unique_ptr<heif_image_handle,decltype(&heif_image_handle_release)> handle(raw_handle,heif_image_handle_release);
            size_check(heif_image_handle_get_width(handle.get()),heif_image_handle_get_height(handle.get()));
            heif_image* raw_image=nullptr;check(heif_decode_image(handle.get(),&raw_image,heif_colorspace_RGB,heif_chroma_interleaved_RGBA,nullptr));std::unique_ptr<heif_image,decltype(&heif_image_release)> image(raw_image,heif_image_release);
            uint32_t width=heif_image_get_width(image.get(),heif_channel_interleaved),height=heif_image_get_height(image.get(),heif_channel_interleaved);size_check(width,height);
            uint32_t profile_size=static_cast<uint32_t>(heif_image_handle_get_raw_color_profile_size(handle.get()));if(profile_size>4*1024*1024)throw std::runtime_error("HEIC color profile is too large");
            std::vector<uint8_t> profile(profile_size);if(profile_size)check(heif_image_handle_get_raw_color_profile(handle.get(),profile.data()));
            std::ofstream output(std::filesystem::path(argv[3]),std::ios::binary);output.write(reinterpret_cast<const char*>(&width),4);output.write(reinterpret_cast<const char*>(&height),4);output.write(reinterpret_cast<const char*>(&profile_size),4);if(profile_size)output.write(reinterpret_cast<const char*>(profile.data()),profile.size());
            int stride=0;auto plane=heif_image_get_plane_readonly(image.get(),heif_channel_interleaved,&stride);for(uint32_t y=0;y<height;y++)output.write(reinterpret_cast<const char*>(plane+y*stride),width*4);if(!output)throw std::runtime_error("Cannot write decoded HEIC pixels");return 0;
        }
        if(argc==7&&wcscmp(argv[1],L"encode")==0)
        {
            int width=_wtoi(argv[3]),height=_wtoi(argv[4]),quality=_wtoi(argv[5]);size_check(width,height);if(quality<1||quality>100)throw std::runtime_error("HEIC quality must be 1..100");
            auto rgba=read(argv[2],uint64_t(width)*height*4);if(rgba.size()!=uint64_t(width)*height*4)throw std::runtime_error("HEIC pixel length does not match dimensions");
            const heif_encoder_descriptor* descriptors[8];int count=heif_get_encoder_descriptors(heif_compression_HEVC,nullptr,descriptors,8);const heif_encoder_descriptor* descriptor=nullptr;
            for(int i=0;i<count;i++)if(strcmp(heif_encoder_descriptor_get_id_name(descriptors[i]),"kvazaar")==0)descriptor=descriptors[i];if(!descriptor)throw std::runtime_error("Bundled Kvazaar HEVC encoder is unavailable");
            heif_encoder* raw_encoder=nullptr;check(heif_context_get_encoder(context.get(),descriptor,&raw_encoder));std::unique_ptr<heif_encoder,decltype(&heif_encoder_release)> encoder(raw_encoder,heif_encoder_release);check(heif_encoder_set_lossy_quality(encoder.get(),quality));
            bool transparent=false;for(size_t i=3;i<rgba.size();i+=4)if(rgba[i]!=255){transparent=true;break;}if(transparent)check(heif_encoder_set_lossless(encoder.get(),1));
            heif_image* raw_image=nullptr;check(heif_image_create(width,height,heif_colorspace_RGB,heif_chroma_interleaved_RGBA,&raw_image));std::unique_ptr<heif_image,decltype(&heif_image_release)> image(raw_image,heif_image_release);check(heif_image_add_plane(image.get(),heif_channel_interleaved,width,height,8));int stride=0;auto plane=heif_image_get_plane(image.get(),heif_channel_interleaved,&stride);for(int y=0;y<height;y++)memcpy(plane+y*stride,rgba.data()+uint64_t(y)*width*4,width*4);
            std::unique_ptr<heif_color_profile_nclx,decltype(&heif_nclx_color_profile_free)> color(heif_nclx_color_profile_alloc(),heif_nclx_color_profile_free);check(heif_nclx_color_profile_set_color_primaries(color.get(),1));check(heif_nclx_color_profile_set_transfer_characteristics(color.get(),13));check(heif_nclx_color_profile_set_matrix_coefficients(color.get(),6));color->full_range_flag=1;check(heif_image_set_nclx_color_profile(image.get(),color.get()));
            heif_image_handle* raw_handle=nullptr;check(heif_context_encode_image(context.get(),image.get(),encoder.get(),nullptr,&raw_handle));std::unique_ptr<heif_image_handle,decltype(&heif_image_handle_release)> handle(raw_handle,heif_image_handle_release);
            std::unique_ptr<FILE,decltype(&fclose)> output(_wfopen(argv[6],L"wb"),fclose);if(!output)throw std::runtime_error("Cannot open encoded HEIC output");heif_writer writer{};writer.writer_api_version=1;writer.write=write_bytes;check(heif_context_write(context.get(),&writer,output.get()));return 0;
        }
        throw std::runtime_error("Usage: citrus-heic decode input output | encode rgba width height quality output");
    }
    catch(const std::exception& error){std::cerr<<error.what()<<"\n";return 1;}
}
